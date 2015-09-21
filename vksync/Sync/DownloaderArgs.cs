using Clee;

namespace vksync.Sync
{
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
}