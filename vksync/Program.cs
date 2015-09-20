﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using Clee;
using YAVAW;
using System.Collections.Immutable;
using System.Threading;
using System.Net;

namespace vkmusics
{
    public class Program
    {
        public static Config config { get; set; } = new Config();

        public static void Main(string[] args)
        {
            var eng = CleeEngine.CreateDefault();
            try
            {
                eng.Execute(args);
            }
            catch (AggregateException ae)
            {
                var messages = ae.Flatten().InnerExceptions.Select(ie => ie.Message);
                Console.WriteLine(string.Join(Environment.NewLine, messages));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.InnerException?.Message);
            }
            return;
        }
    }

    public class AddCommand : ICommand<AddArguments>
    {
        public void Execute(AddArguments args)
        {

        }
    }

    public class AddArguments : ICommandArguments
    {
        public string Token { get; set; }
    }

    [Command(Description = "Syncronize music for accounts", Name = "sync")]
    public class SyncCommand : ICommand<DownloaderArgs>
    {
        public static Config config { get; set; } = new Config();

        public void Execute(DownloaderArgs args)
        {
            args.Token = args.Token ?? config.Get("token");
            args.UserId = args.UserId ?? config.Get("user_id");
            args.Folder = args.Folder ?? config.Get("folder");

            if (string.IsNullOrWhiteSpace(args.Token)) throw new Exception("Token is not specified. Use -token <token>");
            if (string.IsNullOrWhiteSpace(args.Folder)) throw new Exception("Folder is not specified. Use -folder <folderPath>");

            var downloader = new VKDownloader(args, new ConsoleReporter());

            if (downloader.Execute().Result)
            {
                if (args.Save)
                {
                    if (!string.IsNullOrWhiteSpace(args.Token)) config.Set("token", args.Token);
                    if (!string.IsNullOrWhiteSpace(args.UserId)) config.Set("user_id", args.UserId);
                    if (!string.IsNullOrWhiteSpace(args.Folder)) config.Set("folder", args.Folder);
                }

                return;
            }

            return;
        }
    }

    public class Config
    {
        string filePath { get; set; } = Environment.ExpandEnvironmentVariables(@"%APPDATA%\.vkmusic\defaults");

        public Config()
        {
            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }

            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, "{}");
            }
        }

        public string Get(string key)
        {
            var content = File.ReadAllText(filePath);
            JToken jsn = JsonConvert.DeserializeObject(content) as JToken;
            return jsn[key]?.ToObject<string>();
        }

        public void Set(string key, string value)
        {
            var content = File.ReadAllText(filePath);
            JToken jsn = JsonConvert.DeserializeObject(content) as JToken;
            jsn[key] = value;
            File.WriteAllText(filePath, JsonConvert.SerializeObject(jsn));
        }
    }

    public class DownloaderArgs : Clee.ICommandArguments
    {
        [Value(IsOptional = true)]
        public string Token { get; set; }

        [Value(IsOptional = true)]
        public string UserId { get; set; }

        [Value(IsOptional = true)]
        public string Folder { get; set; }

        [Value(IsOptional = true)]

        public bool Save { get; set; }
    }
    public class ConsoleReporter
    {
        ConsoleState state = new ConsoleState();
        BlockingCollection<Action<ConsoleState>> _updateQueue = new BlockingCollection<Action<ConsoleState>>();
        bool isDirty = true;

        int DefaultCursorPosition { get; set; } = Console.CursorTop;

        VirtualConsole console = new VirtualConsole();

        public ConsoleReporter()
        {
            Task.Factory.StartNew(UpdateQueueWorker);
        }

        public void Update(Action<ConsoleState> cs)
        {
            _updateQueue.Add(cs);
        }

        internal void ScheduleRender()
        {
            isDirty = true;
        }

        private void RenderWorker()
        {
            while (true)
            {
                Render();
                Thread.Sleep(100);
            }
        }

        private void UpdateQueueWorker()
        {
            foreach (var update in _updateQueue.GetConsumingEnumerable())
            {
                update(state);
                Render();
            }
        }

        public void Render()
        {
            if (!isDirty) return;

            var sb = ImmutableList<string>.Empty;
            sb = sb.Add(state.CurrentOperation);
            sb = sb.Add("");
            sb = sb.Add(state.CurrentOperationStatus);

            foreach (var item in state.Downloads)
            {
                sb = sb.Add($"[{item.PercentComplete}]: {item.Title}");
            }

            console.Render(sb);
        }
    }

    public class VirtualConsole
    {
        public IImmutableList<string> State { get; set; } = ImmutableList<string>.Empty;

        int maxWidth = 80;

        public VirtualConsole()
        {
            maxWidth = Console.WindowWidth;
        }

        public void Render(IImmutableList<string> newState)
        {
            for (var i = 0; i < Math.Max(newState.Count, State.Count); i++)
            {
                var newStateLine = i < newState.Count ? newState[i] ?? "" : "";
                var oldStateLine = i < State.Count ? State[i] ?? "" : "";

                if (newStateLine != oldStateLine)
                {
                    RenderLine(i, newStateLine);
                }
            }

            Console.SetCursorPosition(0, Math.Max(newState.Count, State.Count));
            
            State = newState;
        }

        private void RenderLine(int row, string line)
        {
            Console.SetCursorPosition(0, row);
            var res = line.Substring(0, Math.Min(line.Length, maxWidth)).PadRight(maxWidth);
            Console.Write(res);
        }

        private string Convert(string inputStr, Encoding a, Encoding b)
        {
            return b.GetString(a.GetBytes(inputStr));
        }
    }
    public class ConsoleState
    {
        public string CurrentOperation { get; set; }
        public string CurrentOperationStatus { get; set; }

        public List<DownloadState> Downloads { get; set; } = new List<DownloadState>();

        public class DownloadState
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string PercentComplete { get; set; }
        }
    }

    public class VKDownloader
    {
        DownloaderArgs args;
        const int AppId = 4509223;
        private VkApi client;
        private ConsoleReporter reporter;

        public VKDownloader(DownloaderArgs args, ConsoleReporter reporter)
        {
            this.args = args;
            this.reporter = reporter;

            if (string.IsNullOrWhiteSpace(args.Folder)) throw new Exception("Folder should be specified! '-folder'");
        }

        string UserId { get; set; }

        internal async Task<bool> Execute()
        {
            reporter.Update(s => s.CurrentOperation = "Communicating with VK");

            client = new VkApi(AppId.ToString(), args.Token);

            reporter.Update(s => s.CurrentOperationStatus = "Getting user info");
            var userInfo = await Users_Get(args.UserId);
            reporter.Update(s => s.CurrentOperation = $"Downloading music from {userInfo.LastName}, {userInfo.FirstName}");

            args.UserId = userInfo.Id;
            args.Folder = Path.Combine(args.Folder, $"{userInfo.FirstName}_{userInfo.LastName}");
            Directory.CreateDirectory(args.Folder);

            reporter.Update(s => s.CurrentOperationStatus = $"Getting full music list");
            var musicInfo = GetMusicInfo().ToList();
            reporter.Update(s => s.CurrentOperationStatus = $"Getting full music list. Done");

            var filesInDestination = GetFilesInDestination().Select(f => f.Title).ToImmutableHashSet();

            var downloadCollection = new BlockingCollection<Music>();

            var tasks = new List<Task>();

            for (var i = 0; i < 16; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    foreach (var music in downloadCollection.GetConsumingEnumerable())
                    {
                        Download(music);
                    }
                }));
            }

            foreach (var music in musicInfo.Where(m =>  !filesInDestination.Contains(CleanFileName(m.Title))))
            {
                downloadCollection.Add(music);
            }

            downloadCollection.CompleteAdding();

            Task.WaitAll(tasks.ToArray());

            return true;
        }

        internal bool Download(Music music)
        {
            var client = new WebClient();

            var downloadStateItem = new ConsoleState.DownloadState { Id = music.Id, Title = music.Title, PercentComplete = " NEW" };

            reporter.Update(s => { s.Downloads.Add(downloadStateItem); });

            client.DownloadProgressChanged += (o, downloadProgress) =>
            {
                reporter.Update(s =>
                {
                    downloadStateItem.PercentComplete = $"{downloadProgress.ProgressPercentage}%".PadLeft(4, ' ');
                });
            };

            var path = Path.Combine(args.Folder, $"{CleanFileName(music.Title)}.mp3");

            client.DownloadFileTaskAsync(new Uri(music.Url), path).Wait();
            
            reporter.Update(s => { downloadStateItem.PercentComplete = "DONE"; });

            return true;

        }

        string ProgressState(long read, long total)
        {
            return $"{(int)Math.Round(read / total * 100.0, 0)}%";
        }

        private void CopyWithProgress(Stream input, Stream output, Action<long> progress)
        {
            byte[] buffer = new byte[4096];
            int read;
            long totalRead = 0;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                totalRead += read;
                progress(totalRead);
            }
        }

        internal IEnumerable<Music> GetFilesInDestination()
        {
            foreach (var file in Directory.EnumerateFiles(args.Folder, "*.mp3"))
            {
                yield return new Music
                {
                    Title = CleanFileName(Path.GetFileNameWithoutExtension(file))
                };
            }
        }

        private static string CleanFileName(string fileName)
        {
            return Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c.ToString(), string.Empty));
        }

        internal IEnumerable<Music> GetMusicInfo()
        {
            var result = new BlockingCollection<Music>();

            Task.Factory.StartNew(async () =>
            {
                int offset = 0;
                int batch = 200;
                int vkCount = 0;
                do
                {
                    var response = await client.CallApiMethod(ApiMethod.audio_get, new Dictionary<string, string>
                    {
                        ["owner_id"] = args.UserId,
                        ["count"] = batch.ToString(),
                        ["offset"] = (offset * batch).ToString()
                    });

                    vkCount = response["count"].ToObject<int>();
                    offset++;

                    foreach (var item in response["items"])
                    {
                        var music = new Music
                        {
                            Title = item["title"].ToObject<string>().Trim(),
                            Artist = item["artist"].ToObject<string>(),
                            Id = item["id"].ToObject<string>(),
                            Url = item["url"].ToObject<string>()
                        };

                        result.Add(music);
                    }

                } while (offset * batch < vkCount);

                result.CompleteAdding();
            });

            return result.GetConsumingEnumerable();
        }

        internal async Task<UserInfo> Users_Get(string id = null)
        {
            var requestData = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(id))
            {
                requestData.Add("user_ids", id);
            }

            var response = await client.CallApiMethod(ApiMethod.users_get, requestData);

            return new UserInfo
            {
                Id = response[0]["id"].ToObject<string>(),
                FirstName = response[0]["first_name"].ToObject<string>(),
                LastName = response[0]["last_name"].ToObject<string>()
            };
        }
    }

    public class Progress
    {
        public long TotalBytes { get; set; }
        public long ProgressBytes { get; set; }

        public int ProgressState
        {
            get
            {
                return (int)Math.Round(ProgressBytes / TotalBytes * 100.0, 0);
            }
        }
    }

    public class UserInfo
    {
        public string Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class Music
    {
        public string Title { get; set; }
        public string Id { get; set; }
        public string Artist { get; set; }
        public string Url { get; set; }
        public string Duration { get; set; }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;

            if (obj is Music)
            {
                var item = obj as Music;

                return item.Title.Equals(Title);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Title.GetHashCode();
        }
    }
}