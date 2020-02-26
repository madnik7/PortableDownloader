Portable Downloader Manager
==========

A library for resuming and multi-part/multi-threaded downloads in .NET written in C#
Advanced Portable Download Manager with simple usage.

The library based on .NET Standard 2.0

### Features
* Resume downloads after restart the manager
* Recusively start or stop all downloads in a folder
* Each download is segemented
* Cross-Platform

Example for usage:
Start or resuming max 3 downloads simultaneously, each download with 4 parts

```C#
// Create a portable storage
using var storage = PortableStorage.Providers.FileStorgeProvider.CreateStorage(@"c:\temp", true, null);

// Create a portable download manager
var dmOptions = new PortableDownloader.DownloadManagerOptions() { Storage = storage };
using var dm = new PortableDownloader.DownloadManager(dmOptions);

dm.Add("file1.zip", new Uri("https://abcd.com/file1.zip"));
dm.Add("file2.zip", new Uri("https://abcd.com/file2.zip"));
dm.Add("folder/file3.zip", new Uri("https://abcd.com/file3.zip"));
dm.Add("folder/file4.zip", new Uri("https://abcd.com/file4.zip"));
dm.Add("folder/file5.zip", new Uri("https://abcd.com/file5.zip"));

// wait for downloads
while (!dm.IsIdle)
    Thread.Sleep(500);

// done
```
