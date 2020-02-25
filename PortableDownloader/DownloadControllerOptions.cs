using PortableStorage;
using System;
using System.Collections.Generic;
using System.Text;

namespace PortableDownloader
{
    class DownloadControllerOptions
    {
        public Uri Uri { get; set; }
        public Storage Storage { get; set; }
        public string DownloadingStreamPath { get; set; }
        public string DownloadingInfoStreamPath { get; set; }
    }
}
