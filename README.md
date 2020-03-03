Portable Downloader Manager
==========

A library for resuming and multi-part/multi-threaded downloads in .NET written in C#
Advanced Portable Download Manager with simple usage.

The library based on .NET Standard 2.0

### Features
* Resume downloads after restart the manager
* Each download is segemented
* Cross-Platform

### Limitation
At the moment the server should report the stream size

### Nuget
https://www.nuget.org/packages/PortableDownloader/

### Usage For Single Download
```C#
using var downloader = new Downloader(new DownloaderOptions()
{ 
    Uri = new Uri("https://abcd.com/file1.zip"), 
    Stream = File.OpenWrite(@"c:\temp\file1.zip")
});

downloader.Start().ContinueWith(x =>
{
    Console.WriteLine("Single File is Downloaded!");
});

while (downloader.IsStarted)
    Thread.Sleep(500);

```


### Usage For Download Manager
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