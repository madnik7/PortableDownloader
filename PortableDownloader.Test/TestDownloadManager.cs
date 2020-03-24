using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using PortableDownloader;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;

namespace PortableDownloader.Test
{
    [TestClass]
    public class TestDownloadManager
    {
        public static string TempPath => Path.Combine(Path.GetTempPath(), "_PortableDownloadManager.Test");


        [ClassInitialize]
        public static void Init(TestContext _)
        {
        }

        private void WaitForAllDownloads(DownloadManager downloadManager)
        {
            while (!downloadManager.IsIdle)
            {
                Thread.Sleep(500);
            }
        }

        public void ForReadME1()
        {

            using var downloader = new Downloader(new DownloaderOptions()
            {
                Uri = new Uri("https://abcd.com/file1.zip"),
                Stream = File.OpenWrite(@"c:\temp\file1.zip")
            });

            downloader.Start().ContinueWith(x =>
            {
                Console.WriteLine("Single File Downloaded!");
            });

            while (downloader.IsStarted)
                Thread.Sleep(500);
        }

        public void ForReadME2()
        {
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(@"c:\temp", true, null);
            var downloadController = DownloadController.Create(new DownloadControllerOptions()
            {
                Uri = new Uri("https://abcd.com/file1.zip"),
                Storage = storage,
                DownloadPath = "file1"
            });

            downloadController.Start().ContinueWith(x =>
            {
                Console.WriteLine("Single File Downloaded!");
            });

            while (downloadController.IsStarted)
                Thread.Sleep(500);

        }

        public void ForReadME3()
        {
            // Create a portable storage
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(@"c:\temp", true, null);

            // Create a portable download manager
            var dmOptions = new DownloadManagerOptions() { Storage = storage, MaxOfSimultaneousDownloads = 3 };
            using var dm = new DownloadManager(dmOptions);

            dm.Add("file1.zip", new Uri("https://abcd.com/file1.zip"));
            dm.Add("file2.zip", new Uri("https://abcd.com/file2.zip"));
            dm.Add("folder/file3.zip", new Uri("https://abcd.com/file3.zip"));
            dm.Add("folder/file4.zip", new Uri("https://abcd.com/file4.zip"));
            dm.Add("folder/file5.zip", new Uri("https://abcd.com/file5.zip"));

            // wait for downloads
            while (!dm.IsIdle)
                Thread.Sleep(500);

            // done
        }

        [TestMethod]
        public void Test_Add_Start_Stop_Restore_Cancel()
        {

            var path = Path.Combine(TempPath, Guid.NewGuid().ToString());
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(path, true, null);

            var uri = new Uri("https://download.sysinternals.com/files/SysinternalsSuite-ARM64.zip");
            var dmOptions = new DownloadManagerOptions() { Storage = storage, MaxOfSimultaneousDownloads = 100 };
            using (var dm = new DownloadManager(dmOptions))
            {
                dm.Add("file1", uri, false);
                dm.Add("file2", uri, false);
                dm.Add("file3", uri, false);
                dm.Add("folder1/file1", uri, false);
                dm.Add("folder1/file2", uri, false);
                dm.Add("folder1/file3", uri, false);

                // check the number of added items
                Assert.AreEqual(6, dm.Items.Length, "Invalid number of added items");
                Assert.AreEqual(6, dm.GetItems("/").Length, "Invalid number of added items");
                Assert.IsTrue(dm.Items.All(x => x.State == DownloadState.Stopped), "all items but has been in none state");

                // start downloading
                dm.Start("file1");
                dm.Start("folder1/file1");
                dm.Stop("file3");

                // check number of started item
                Assert.AreEqual(2, dm.Items.Count(x => x.IsStarted), "invalid number of started items");
                WaitForAllDownloads(dm);

                // check for errors
                Assert.IsFalse(dm.Items.Any(x => x.State == DownloadState.Error), "there is an error in downloads");

                // check for downloaded stream
                Assert.IsTrue(storage.EntryExists("file1"));
                Assert.IsTrue(storage.EntryExists("folder1/file1"));
                Assert.AreEqual(2, dm.Items.Count(x => x.State == DownloadState.Finished), "2 items must has been finished");

                // check remote finished items
                dm.RemoveFinishedItems();
                Assert.AreEqual(dm.Items.Length, 4, "Invalid number of remained items");

                // download another item
                dm.Start("file3");

                // download another item and cancel it
                dm.Start("folder1/file3");
                dm.Cancel("folder1/file3");

                WaitForAllDownloads(dm);

                // check cancel result
                Assert.AreEqual(1, dm.Items.Count(x => x.State == DownloadState.Finished), "2 items must be finished");
                Assert.IsTrue(storage.EntryExists("file3"));
                Assert.IsFalse(storage.EntryExists($"folder1/file3{dmOptions.DownloadingExtension}"), "item hasn't deleted");
                Assert.IsFalse(storage.EntryExists($"folder1/file3{dmOptions.DownloadingInfoExtension}"), "item hasn't deleted");
            }

            //check restoring
            using (var dm = new DownloadManager(dmOptions))
            {
                Assert.AreEqual(3, dm.Items.Length, 3, "Invalid number of added items");
                Assert.AreEqual(1, dm.Items.Count(x => x.State == DownloadState.Finished), "invalid number of finished items");
                Assert.AreEqual(2, dm.Items.Count(x => x.State == DownloadState.Stopped), "invalid number of not started items");

                dm.Start("folder1/file2");
                Assert.AreEqual(1, dm.Items.Count(x => x.IsStarted), "invalid number of started items");
                Assert.AreEqual(2, dm.Items.Count(x => x.IsIdle), "invalid number of ilde items");
            }

            // restore downloads after restart
            using (var dm = new DownloadManager(dmOptions))
            {
                WaitForAllDownloads(dm);
                Assert.IsTrue(storage.EntryExists("folder1/file2"));
            }
        }


        //[TestMethod]
        public void Test_Foo()
        {
            var path = Path.Combine(TempPath, Guid.NewGuid().ToString());
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(path, true, null);

            var uri = new Uri("bgfile");
            var dmOptions = new DownloadManagerOptions() { Storage = storage, MaxOfSimultaneousDownloads = 100 };
            using var dm = new DownloadManager(dmOptions);
            dm.Add("file1", uri);
            dm.Start("file1");
            WaitForAllDownloads(dm);

            Assert.IsTrue(storage.EntryExists("file1"));

        }

        [TestMethod]
        public void Test_Download_must_start_if_finished_file_doesnot_exist()
        {
            var path = Path.Combine(TempPath, Guid.NewGuid().ToString());
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(path, true, null);

            var uri = new Uri("https://download.sysinternals.com/files/SysinternalsSuite-ARM64.zip");
            var dmOptions = new DownloadManagerOptions() { Storage = storage};
            using var dm = new DownloadManager(dmOptions);

            dm.Add("file1", uri, false);
            dm.Add("file1", uri, true);
            Assert.IsFalse(dm.IsIdle, "dowload is not started after second add");
            
            dm.Add("file1", uri);
            WaitForAllDownloads(dm);

            storage.DeleteStream("file1");

            dm.Add("file1", uri);
            WaitForAllDownloads(dm);
            Assert.IsTrue(storage.StreamExists("file1"));
        }


        static bool StreamEquals(Stream stream1, Stream stream2)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(stream1).SequenceEqual(md5.ComputeHash(stream2));
        }

        [TestMethod]
        public async Task Test_Downloader_Stop()
        {
            using var mem1 = new MemoryStream();
            var uri = new Uri("https://download.sysinternals.com/files/SysinternalsSuite-ARM64.zip");
            using var downloader = new Downloader(new DownloaderOptions() { Stream = mem1, Uri = uri, PartSize = 10000, AutoDisposeStream = false });
            Assert.AreEqual(DownloadState.None, downloader.DownloadState, "state should be none before start");

            var task = downloader.Start();
            downloader.Stop();

            try
            {
                await task;
                Assert.Fail("OperationCanceledException was expected!");
            }
            catch (OperationCanceledException) { }

            Assert.AreEqual(DownloadState.Stopped, downloader.DownloadState);
        }


        [TestMethod]
        public async Task Test_Downloader_Start()
        {
            using var mem1 = new MemoryStream(500000);
            var uri = new Uri("https://download.sysinternals.com/files/SysinternalsSuite-ARM64.zip");
            using var downloader = new Downloader(new DownloaderOptions() { Stream = mem1, Uri = uri, PartSize = 10000, AutoDisposeStream = false });

            Assert.AreEqual(DownloadState.None, downloader.DownloadState, "state should be none before start");

            // start downloading by downloader
            var downloaderTask = downloader.Start();

            // start downloading by simple http client download
            using var mem2 = new MemoryStream(500000);
            using var httpClient = new HttpClient();
            var httpClientTask = httpClient.GetStreamAsync(uri);
            (await httpClientTask).CopyTo(mem2);

            await Task.WhenAll(downloaderTask, httpClientTask);
            Assert.AreEqual(DownloadState.Finished, downloader.DownloadState, "state should be finished after start");

            // compare two stream
            mem1.Position = 0;
            mem2.Position = 0;
            Assert.IsTrue(StreamEquals(mem1, mem2));
            Assert.AreEqual(mem2.Length, downloader.TotalSize, "invalid TotalSize");
        }

        [TestMethod]
        public async Task Test_Downloader_Start_NoResume()
        {
            using var mem1 = new MemoryStream();
            var uri = new Uri("https://raw.githubusercontent.com/madnik7/PortableDownloader/master/README.md");
            using var downloader = new Downloader(new DownloaderOptions() { Stream = mem1, Uri = uri, PartSize = 10000, AutoDisposeStream = false, AllowResuming = false });

            // start downloading by downloader
            var downloaderTask = downloader.Start();

            // start downloading by simple http client download
            using var mem2 = new MemoryStream();
            using var httpClient = new HttpClient();
            var httpClientTask = httpClient.GetStreamAsync(uri);
            (await httpClientTask).CopyTo(mem2);

            await Task.WhenAll(downloaderTask, httpClientTask);
            Assert.IsFalse(downloader.IsResumingSupported, "the test link should not support resuming");
            Assert.AreEqual(DownloadState.Finished, downloader.DownloadState, "state should be finished after start");

            // compare two stream
            mem1.Position = 0;
            mem2.Position = 0;
            Assert.IsTrue(StreamEquals(mem1, mem2));
            Assert.AreEqual(mem2.Length, downloader.TotalSize, "invalid TotalSize");
        }


        [TestMethod]
        public async Task Test_Downloader_Error()
        {
            using var mem = new MemoryStream(1000000);
            var uri = new Uri("https://download.sysinternals.com/files/not-exists-4252336.zip");
            using var downloader = new Downloader(new DownloaderOptions() { Stream = mem, Uri = uri, PartSize = 10000, AutoDisposeStream = false });

            // start downloading by downloader
            try
            {
                await downloader.Start();
            }
            catch { }

            // start downloading by simple download
            Assert.AreEqual(DownloadState.Error, downloader.DownloadState, "state should be error after start");
        }


        [TestMethod]
        public void Test_Storage_Download()
        {

            var path = Path.Combine(TempPath, Guid.NewGuid().ToString());
            using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(path, true, null);

            var uri = new Uri("https://download.sysinternals.com/files/SysinternalsSuite-ARM64.zip");
            var dmOptions = new DownloadManagerOptions() { Storage = storage, MaxOfSimultaneousDownloads = 100 };
            using (var dm = new DownloadManager(dmOptions))
            {
                dm.Add("file1", uri, false);
                dm.Add("file2", uri, false);
                dm.Add("folder1/file1", uri, false);
                dm.Add("folder1/file2", uri, false);
                dm.Add("folder1/file3", uri, false);

                // no item should be started
                Assert.AreEqual(0, dm.Items.Count(x => x.IsStarted));

                // check the number of added items
                dm.Start("folder1");
                Assert.AreEqual(3, dm.Items.Count(x => x.IsStarted));

                WaitForAllDownloads(dm);
                Assert.AreEqual(2, dm.Items.Count(x => x.State == DownloadState.Stopped), "invalid number of item with none state");
                Assert.AreEqual(3, dm.Items.Count(x => x.State == DownloadState.Finished), "invalid number of item with finish state");
            }

            Assert.IsTrue(storage.EntryExists($"folder1/file1"), "item has not been downloaded!");
            Assert.IsTrue(storage.EntryExists($"folder1/file2"), "item has not been downloaded!");
            Assert.IsTrue(storage.EntryExists($"folder1/file3"), "item has not been downloaded!");
        }


        [ClassCleanup]
        public static void ClassCleanup()
        {
            if (Directory.Exists(TempPath))
                Directory.Delete(TempPath, true);
        }
    }
}
