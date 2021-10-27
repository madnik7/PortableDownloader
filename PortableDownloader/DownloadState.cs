using System.Text.Json.Serialization;

namespace PortableDownloader
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DownloadState
    {
        None,
        Initializing,
        Initialized,
        Downloading,
        Finished,
        Stopped,
        Stopping,
        Error
    }
}