using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PortableDownloader.Sample
{
    class Program
    {
        static void ShowHelp()
        {
            Console.WriteLine("Portable Downloader Sample");
            Console.WriteLine("");
            Console.WriteLine("dl url path [/PartSize x] [/MaxPartCount x] [WriteBufferSize x] /DisableResuming");
        }

        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                ShowHelp();
                return;
            }

            var dmOptions = new DownloadManagerOptions() { RestoreLastList = false };
            var continueAfterRestart = false;

            var url = args[0];
            var path = args[1];

            //process argument
            var lastKey = "";
            foreach (var item in args)
            {
                if (lastKey.Equals("/PartSize", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.PartSize = long.Parse(item);

                else if (lastKey.Equals("/MaxPartCount", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.MaxPartCount = int.Parse(item);

                else if (lastKey.Equals("/WriteBufferSize", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.WriteBufferSize = int.Parse(item);

                else if (item.Equals("/DisableResuming", StringComparison.InvariantCultureIgnoreCase))
                    dmOptions.AllowResuming = false;

                else if (item.Equals("/ContinueAfterRestart", StringComparison.InvariantCultureIgnoreCase))
                    continueAfterRestart = true;

                lastKey = item;
            }

            if (args.Contains("/t"))
                _ = DownloadByHttpClient(url, path);

            // download by portable
            DownloadByPortableDownloader(url, path, dmOptions, continueAfterRestart);

        }

        static void ReportSpeed(DateTime startTime, long size)
        {
            var totalSeconds = Math.Max(1, (int)(DateTime.Now - startTime).TotalSeconds);
            var bytePerSeconds = (int)(size / totalSeconds);
            var speed = ((float)bytePerSeconds / 1000000).ToString("0.00");
            Console.WriteLine($"** Download has finished. Size: {size}, Time: {totalSeconds} Seconds, Speed: {speed} MB/s");
            Console.WriteLine();
        }

        static async Task DownloadByHttpClient(string url, string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var startTime = DateTime.Now;
            if (File.Exists(path)) File.Delete(path);

            Console.WriteLine("Download using HttpClient: ");
            var client = new HttpClient();
            await using var stream = await client.GetStreamAsync(url);
            await using var fs = new FileStream(path, FileMode.CreateNew);
            await stream.CopyToAsync(fs);
            ReportSpeed(startTime, new FileInfo(path).Length);
        }

        static void DownloadByPortableDownloader(string url, string path, DownloadManagerOptions options, bool continueAfterRestart)
        {
            var startTime = DateTime.Now;

            Console.WriteLine($"Download using PortableDownloader. \nMaxPartCount: {options.MaxPartCount}, \nPartSize: {options.PartSize}, \nAllowResuming: {options.AllowResuming}, \nWriteBufferSize: {options.WriteBufferSize}");
            Console.WriteLine();
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(Path.GetDirectoryName(path), true);
            options.Storage = storage;
            using var dm = new DownloadManager(options);
            var streamPath = Path.GetFileName(path);

            //delete old download
            if (!continueAfterRestart)
            {
                if (storage.StreamExists(streamPath))
                    storage.DeleteStream(streamPath);
                dm.Cancel(streamPath);
            }

            dm.Add(streamPath, new Uri(url));
            while (!dm.IsIdle)
            {
                var item = dm.GetItem();
                var totalSeconds = Math.Max(1, (int)(DateTime.Now - startTime).TotalSeconds);
                var speed = ((float)item.BytesPerSecond / 1000000).ToString("0.00");
                Console.WriteLine($"Downloaded: {item.CurrentSize} / { item.TotalSize }, Timer: {totalSeconds} Seconds, Speed: {speed} MB/s ");
                Thread.Sleep(1000);
            }

            ReportSpeed(startTime, dm.GetItem().TotalSize);
        }
    }
}
