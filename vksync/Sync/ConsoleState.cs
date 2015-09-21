using System.Collections.Immutable;

namespace vksync.Sync
{
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
}