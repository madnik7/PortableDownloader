using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PortableDownloader
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum DownloadState
    {
        None,
        Initializing,
        Initialized,
        Downloading,
        Finished,
        Stopped,
        Error
    }
}
