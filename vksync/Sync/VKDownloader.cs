using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using vksync.Core;
using YAVAW;

namespace vksync.Sync
{
    public class VKDownloader
    {
        readonly DownloaderArgs _args;
        const int AppId = 4509223;
        private VkApi _client;
        private readonly ConsoleReporter<ConsoleState> _reporter;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        public VKDownloader(DownloaderArgs args)
        {
            _args = args;

            _reporter = new ConsoleReporter<ConsoleState>(new ConsoleState(), (state) =>
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
                    var avgSpeed = totalMb / _stopwatch.Elapsed.TotalSeconds;

                    sb = sb.Add($"So far, downloaded {state.ItemsDownloaded} songs");
                    sb = sb.Add($"{totalMb.ToString("0.00")} Mb - {avgSpeed.ToString("0.00")} Mb/s");

                    if (state.TotalSongsToDownload > 0)
                    {
                        var songAvgSize = totalMb / (double)(state.ItemsDownloaded == 0 ? 1 : state.ItemsDownloaded);
                        var estSizeToDownload = songAvgSize*state.TotalSongsToDownload;
                        
                        var eta = TimeSpan.FromSeconds((estSizeToDownload - totalMb) / avgSpeed);
                        
                        sb = sb.Add($"Elapsed: {_stopwatch.Elapsed.ToString("00:00:00")} ETA: {eta.ToString("00:00:00")}");
                    }
                }

                return sb;
            });

            if (string.IsNullOrWhiteSpace(args.Folder)) throw new Exception("Folder should be specified! '-folder'");
        }

        internal async Task<bool> Execute()
        {
            _reporter.Update(s => s.CurrentOperation = "Communicating with VK");

            _client = new VkApi(AppId.ToString(), _args.Token);

            _reporter.Update(s => s.CurrentOperationStatus = "Getting user info");
            var userInfo = await Users_Get(_args.UserId);
            _reporter.Update(s => s.CurrentOperation = $"Downloading music from {userInfo.LastName}, {userInfo.FirstName} to {_args.Folder}");

            _args.UserId = userInfo.Id;
            _args.Folder = Path.Combine(_args.Folder, $"{userInfo.FirstName}_{userInfo.LastName}");
            Directory.CreateDirectory(_args.Folder);

            _reporter.Update(s => s.CurrentOperationStatus = $"Getting full music list");
            var musicInfo = GetMusicInfo().ToList();
            _reporter.Update(s => s.CurrentOperationStatus = $"Getting full music list. Done");

            var filesInDestination = GetFilesInDestination().Select(f => f.Title).ToImmutableHashSet();

            var downloadCollection = new BlockingCollection<Music>();

            var tasks = new List<Task>();

            SpinWorkers(downloadCollection, tasks, 4);

            _stopwatch.Start();

            var songsToDownload = musicInfo.Where(m => !filesInDestination.Contains(CleanFileName(m.Title))).ToList();
            _reporter.Update(s => s.TotalSongsToDownload = songsToDownload.Count);

            foreach (var music in songsToDownload)
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
                    foreach (var music in downloadCollection.GetConsumingEnumerable())
                    {
                        Download(music);

                        _reporter.Update(state =>
                        {
                            state.ItemsDownloaded++;
                            return state;
                        });
                    }
                }));
            }
        }

        internal bool Download(Music music)
        {
            var targetFilename = Path.Combine(_args.Folder, $"{CleanFileName(music.Title)}.mp3");

            if (File.Exists(targetFilename)) return true;

            using (var client = new WebClient())
            {
                var downloadStateItem = new ConsoleState.DownloadState { Id = music.Id, Title = music.Title, PercentComplete = " NEW" };

                _reporter.Update(s => { s.Downloads = s.Downloads.Add(downloadStateItem); });
                long lastBytes = 0;

                var locker = new ManualResetEventSlim(false);

                client.DownloadProgressChanged += (o, downloadProgress) =>
                {
                    _reporter.Update(s =>
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

                _reporter.Update(s => { downloadStateItem.PercentComplete = "DONE"; });

                return true;
            }
        }
         
        internal IEnumerable<Music> GetFilesInDestination()
        {
            foreach (var file in Directory.EnumerateFiles(_args.Folder, "*.mp3"))
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
                    var response = await _client.CallApiMethod(ApiMethod.audio_get, new Dictionary<string, string>
                    {
                        ["owner_id"] = _args.UserId,
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

            var response = await _client.CallApiMethod(ApiMethod.users_get, requestData);

            return new UserInfo
            {
                Id = response[0]["id"].ToObject<string>(),
                FirstName = response[0]["first_name"].ToObject<string>(),
                LastName = response[0]["last_name"].ToObject<string>()
            };
        }
    }
}