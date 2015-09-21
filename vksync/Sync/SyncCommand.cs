using System;
using Clee;
using vksync.Core;

namespace vksync.Sync
{
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
}