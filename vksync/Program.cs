using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Text;
using Clee;
using YAVAW;
using System.Collections.Immutable;
using System.Threading;
using System.Net;
using System.Diagnostics;

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

    public interface IAction<T>
    {
        T Act(T state);
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

            var downloader = new VKDownloader(args);

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


    public class ConsoleReporter<T>
    {
        T state = default(T);

        BlockingCollection<Action<T>> _updateQueue = new BlockingCollection<Action<T>>();
        bool isDirty = true;

        VirtualConsole console = new VirtualConsole();
        private readonly Func<T, IList<string>> Renderer;
        Stopwatch stopwatch = new Stopwatch();
        private TimeSpan lastrun;

        public ConsoleReporter(T initialState, Func<T, IList<string>> renderer)
        {
            this.state = initialState;
            this.Renderer = renderer;
            Task.Factory.StartNew(UpdateQueueWorker);
            Task.Factory.StartNew(RenderWorker);
            stopwatch.Start();
            lastrun = stopwatch.Elapsed;
        }

        public void Update(Action<T> cs)
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
                if (stopwatch.Elapsed - lastrun > TimeSpan.FromSeconds(1 / 30.0))
                {
                    Render();
                    lastrun = stopwatch.Elapsed;
                }
                Thread.SpinWait(10);
            }
        }

        private void UpdateQueueWorker()
        {
            foreach (var update in _updateQueue.GetConsumingEnumerable())
            {
                update(state);
                isDirty = true;
            }
        }

        public void Render()
        {
            if (!isDirty) return;
            var sb = Renderer(state);
            console.Render(sb);
            isDirty = false;
        }
    }

    public class VirtualConsole
    {
        public IImmutableList<string> State { get; set; } = ImmutableList<string>.Empty;
        int DefaultCursorPosition { get; set; } = Console.CursorTop;

        int maxWidth = 80;

        public VirtualConsole()
        {
            maxWidth = Console.WindowWidth;
        }

        public void Render(IList<string> newState)
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

            Console.SetCursorPosition(0, DefaultCursorPosition + Math.Max(newState.Count, State.Count));

            State = ImmutableList<string>.Empty.AddRange(newState);
        }

        private void RenderLine(int row, string line)
        {
            Console.SetCursorPosition(0, DefaultCursorPosition + row);
            var res = line.Substring(0, Math.Min(line.Length, maxWidth)).PadRight(maxWidth, ' ');
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

        public long TotalBytes { get; set; } = 0;

        public ImmutableList<DownloadState> Downloads { get; set; } = ImmutableList<DownloadState>.Empty;

        public class DownloadState
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string PercentComplete { get; set; }
            public long TotalBytes { get; set; }
        }
    }

    public class VKDownloader
    {
        DownloaderArgs args;
        const int AppId = 4509223;
        private VkApi client;
        private ConsoleReporter<ConsoleState> reporter;
        private Stopwatch stopwatch = new Stopwatch();

        public VKDownloader(DownloaderArgs args)
        {
            this.args = args;

            reporter = new ConsoleReporter<ConsoleState>(new ConsoleState(), (state) =>
            {
                var sb = ImmutableList<string>.Empty;

                sb = sb.Add(state.CurrentOperation);
                sb = sb.Add("");

                sb = sb.Add(state.CurrentOperationStatus);
                sb = sb.Add("");

                foreach (var item in state.Downloads)
                {
                    sb = sb.Add($"[{item.PercentComplete}]: {item.Title}");
                }

                sb = sb.Add("");

                if (state.TotalBytes > 0)
                {
                    var totalMb = state.TotalBytes / (double)(1024 * 1024);
                    var speed = totalMb / stopwatch.Elapsed.TotalSeconds;

                    sb = sb.Add($"So far, downloaded {totalMb.ToString("0.00")} Mb - {speed.ToString("0.00")} Mb/s");
                }

                return sb;
            });

            if (string.IsNullOrWhiteSpace(args.Folder)) throw new Exception("Folder should be specified! '-folder'");
        }

        string UserId { get; set; }

        internal async Task<bool> Execute()
        {
            reporter.Update(s => s.CurrentOperation = "Communicating with VK");

            client = new VkApi(AppId.ToString(), args.Token);

            reporter.Update(s => s.CurrentOperationStatus = "Getting user info");
            var userInfo = await Users_Get(args.UserId);
            reporter.Update(s => s.CurrentOperation = $"Downloading music from {userInfo.LastName}, {userInfo.FirstName} to {args.Folder}");

            args.UserId = userInfo.Id;
            args.Folder = Path.Combine(args.Folder, $"{userInfo.FirstName}_{userInfo.LastName}");
            Directory.CreateDirectory(args.Folder);

            reporter.Update(s => s.CurrentOperationStatus = $"Getting full music list");
            var musicInfo = GetMusicInfo().ToList();
            reporter.Update(s => s.CurrentOperationStatus = $"Getting full music list. Done");

            var filesInDestination = GetFilesInDestination().Select(f => f.Title).ToImmutableHashSet();

            var downloadCollection = new BlockingCollection<Music>();

            var tasks = new List<Task>();

            SpinWorkers(downloadCollection, tasks, 4);

            stopwatch.Start();

            foreach (var music in musicInfo.Where(m => !filesInDestination.Contains(CleanFileName(m.Title))))
            {
                downloadCollection.Add(music);
            }

            downloadCollection.CompleteAdding();

            Task.WaitAll(tasks.ToArray());

            return true;
        }

        private void SpinWorkers(BlockingCollection<Music> downloadCollection, List<Task> tasks, int workersCount)
        {
            for (var i = 0; i < workersCount; i++)
            {
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    int itemsDownloaded = 0;

                    foreach (var music in downloadCollection.GetConsumingEnumerable())
                    {
                        Download(music);
                        itemsDownloaded++;
                    }
                }));
            }
        }

        internal bool Download(Music music)
        {
            var targetFilename = Path.Combine(args.Folder, $"{CleanFileName(music.Title)}.mp3");

            if (File.Exists(targetFilename)) return true;

            using (var client = new WebClient())
            {
                var downloadStateItem = new ConsoleState.DownloadState { Id = music.Id, Title = music.Title, PercentComplete = " NEW" };

                reporter.Update(s => { s.Downloads = s.Downloads.Add(downloadStateItem); });
                long lastBytes = 0;

                var locker = new ManualResetEventSlim(false);

                client.DownloadProgressChanged += (o, downloadProgress) =>
                {
                    reporter.Update(s =>
                    {
                        downloadStateItem.PercentComplete = $"{downloadProgress.ProgressPercentage}%".PadLeft(4, ' ');

                        s.TotalBytes += downloadProgress.BytesReceived - lastBytes;
                        lastBytes = downloadProgress.BytesReceived;
                    });
                };

                client.DownloadFileCompleted += (o, completed) =>
                {
                    locker.Set();
                };

                client.DownloadFileAsync(new Uri(music.Url), targetFilename);

                locker.Wait();

                reporter.Update(s => { downloadStateItem.PercentComplete = "DONE"; });

                return true;
            }
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
                yield return new Music { Title = CleanFileName(Path.GetFileNameWithoutExtension(file)) };
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
