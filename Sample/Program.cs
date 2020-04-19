using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace PortableDownloader.Sample
{
    class Program
    {
        static void ShowHelp()
        {
            Console.WriteLine("Portable Downloader Sample");
            Console.WriteLine("");
            Console.WriteLine("dl url path [/PartSize x] [/MaxPartCount x] [WriteBuffer x] /DisableResuming [/WebclientPath x]");
        }

        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                ShowHelp();
                return;
            }

            var dmOptions = new DownloadManagerOptions();
            var webclientPath = "";

            var url = args[0];
            var path = args[1];

            //process argument
            var lastKey = "";
            foreach (var item in args)
            {
                if (lastKey.Equals("/WebclientPath", StringComparison.InvariantCultureIgnoreCase))
                    webclientPath = item;

                else if (lastKey.Equals("/PartSize", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.PartSize = long.Parse(item);

                else if (lastKey.Equals("/MaxPartCount", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.MaxPartCount = int.Parse(item);

                else if (lastKey.Equals("/WriteBufferSize", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.WriteBufferSize = int.Parse(item);

                else if (item.Equals("/DisableResuming", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.AllowResuming = false;

                else if (item.Equals("/SaveStates", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.AllowResuming = false;


                lastKey = item;
            }

            // download webclient
            if (!string.IsNullOrEmpty(webclientPath)) DownloadByWebClient(url, webclientPath);

            // download by portable
            DownloadByPortableDownloader(url, path, dmOptions);

        }

        static void DownloadByWebClient(string url, string path)
        {
            var startTime = DateTime.Now;

            Console.WriteLine("Download using webClient: ");
            var webClient = new WebClient();
            webClient.DownloadFile(url, path);

            var size = new FileInfo(path).Length;
            var totalSeconds = Math.Max(1, (int)(DateTime.Now - startTime).TotalSeconds);
            var bytePerSeconds = (int)(size / totalSeconds);
            var speed = ((float)bytePerSeconds / 1000000).ToString("0.00");
            Console.WriteLine($"** WebClient has finished. Size: {size}, Time: {totalSeconds} Seconds, Speed: {speed} MB/s");
            Console.WriteLine();
        }


        static void DownloadByPortableDownloader(string url, string path, DownloadManagerOptions options)
        {
            var startTime = DateTime.Now;

            Console.WriteLine($"Download using PortableDownloader. MaxPartCount: {options.MaxPartCount}, PartSize: {options.PartSize}, AllowResuming: {options.AllowResuming}, WriteBufferSize: {options.WriteBufferSize}");
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(Path.GetDirectoryName(path), true, null);
            options.Storage = storage;
            using var dm = new DownloadManager(options);


            var streamPath = Path.GetFileName(path);

            //cleanup
            dm.Cancel(streamPath);

            dm.Add(streamPath, new Uri(url));
            while (!dm.IsIdle)
            {
                var item = dm.GetItem();
                var totalSeconds2 = Math.Max(1, (int)(DateTime.Now - startTime).TotalSeconds);
                var speed2 = ((float)item.BytesPerSecond / 1000000).ToString("0.00");
                Console.WriteLine($"Downloaded: {item.CurrentSize} / { item.TotalSize }, Timer: {totalSeconds2} Seconds, Speed: {speed2} MB/s ");
                Thread.Sleep(1000);
            }

            var size = dm.GetItem().TotalSize;
            var totalSeconds = Math.Max(1, (int)(DateTime.Now - startTime).TotalSeconds);
            var bytePerSeconds = (int)(size / totalSeconds);
            var speed = ((float)bytePerSeconds / 1000000).ToString("0.00");
            
            Console.WriteLine();
            Console.WriteLine($"** DM has finished. Size: {size}, Timer: {totalSeconds} Seconds, Speed: {speed} MB/s");

        }
    }
}
