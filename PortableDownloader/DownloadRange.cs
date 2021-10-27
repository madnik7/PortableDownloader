namespace PortableDownloader
{
    public class DownloadRange
    {
        public long From { get; set; }
        public long To { get; set; }
        public long CurrentOffset { get; set; }
        public bool IsDone { get; set; }
    }
}