using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using System.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using Nick.Plugin.Jellyscrub.Configuration;

namespace Nick.Plugin.Jellyscrub.Drawing;

public class VideoProcessor
{
    private readonly ILogger<VideoProcessor> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly PluginConfiguration _config;

    public VideoProcessor(
        ILogger<VideoProcessor> logger,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _config = JellyscrubPlugin.Instance!.Configuration;
    }

    /*
     * Entry point to tell VideoProcessor to generate BIF from item
     */
    public async Task Run(BaseItem item, CancellationToken cancellationToken)
    {
        var mediaSources = ((IHasMediaSources)item).GetMediaSources(false)
            .ToList();

        foreach (var mediaSource in mediaSources)
        {
            foreach (var width in _config.WidthResolutions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Run(item, mediaSource, width, _config.Interval, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task Run(BaseItem item, MediaSourceInfo mediaSource, int width, int interval, CancellationToken cancellationToken)
    {
        if (!HasBif(item, _fileSystem, width))
        {
            await BifWriterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!HasBif(item, _fileSystem, width))
                {
                    await CreateBif(item, width, interval, mediaSource, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                BifWriterSemaphore.Release();
            }
        }
    }

    /*
     * Methods for getting storage paths of BIFs
     */
    private bool HasBif(BaseItem item, IFileSystem fileSystem, int width)
    {
        return !string.IsNullOrWhiteSpace(GetExistingBifPath(item, fileSystem, width));
    }

    private static string? GetExistingBifPath(BaseItem item, IFileSystem fileSystem, int width)
    {
        var path = GetLocalBifPath(item, width);

        if (fileSystem.FileExists(path))
        {
            return path;
        }

        path = GetInternalBifPath(item, width);

        if (fileSystem.FileExists(path))
        {
            return path;
        }

        return null;
    }

    private static string GetNewBifPath(BaseItem item, int width)
    {
        if (JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving)
        {
            return GetLocalBifPath(item, width);
        }

        return GetInternalBifPath(item, width);
    }

    private static string GetLocalBifPath(BaseItem item, int width)
    {
        var folder = item.ContainingFolderPath;
        var filename = Path.GetFileNameWithoutExtension(item.Path);
        filename += "-" + width.ToString(CultureInfo.InvariantCulture) + ".bif";

        return Path.Combine(folder, filename);
    }

    private static string GetInternalBifPath(BaseItem item, int width)
    {
        return Path.Combine(item.GetInternalMetadataPath(), "trickplay", width.ToString(CultureInfo.InvariantCulture) + ".bif");
    }

    /*
     * Bif Creation
     */
    private static readonly SemaphoreSlim BifWriterSemaphore = new SemaphoreSlim(1, 1);

    private Task CreateBif(BaseItem item, int width, int interval, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        var path = GetNewBifPath(item, width);

        return CreateBif(path, width, interval, item, mediaSource, cancellationToken);
    }

    private async Task CreateBif(string path, int width, int interval, BaseItem item, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating trickplay files at {0} width, for {1}", width, mediaSource.Path);

        var protocol = mediaSource.Protocol;

        var tempDirectory = Path.Combine(_appPaths.TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var videoStream = mediaSource.VideoStream;

            var inputPath = mediaSource.Path;

            await JellyscrubPlugin.Instance!.OldMediaEncoder!.ExtractVideoImagesOnInterval(inputPath, mediaSource.Container, videoStream, mediaSource, mediaSource.Video3DFormat,
                    TimeSpan.FromSeconds(10), tempDirectory, "img_", width, cancellationToken)
                    .ConfigureAwait(false);

            var images = _fileSystem.GetFiles(tempDirectory, new string[] { ".jpg" }, false, false)
                .Where(img => string.Equals(img.Extension, ".jpg", StringComparison.Ordinal))
                .OrderBy(i => i.FullName)
                .ToList();

            var bifTempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N"));

            using (var fs = new FileStream(bifTempPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                await CreateBif(fs, images, interval).ConfigureAwait(false);
            }

            _libraryMonitor.ReportFileSystemChangeBeginning(path);

            try
            {
                Directory.CreateDirectory(Directory.GetParent(path).FullName);
                File.Copy(bifTempPath, path, true);
            }
            finally
            {
                _libraryMonitor.ReportFileSystemChangeComplete(path, false);
            }
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    public async Task CreateBif(Stream stream, List<FileSystemMetadata> images, int interval)
    {
        var magicNumber = new byte[] { 0x89, 0x42, 0x49, 0x46, 0x0d, 0x0a, 0x1a, 0x0a };
        await stream.WriteAsync(magicNumber, 0, magicNumber.Length);

        // Version
        var bytes = GetBytes(0);
        await stream.WriteAsync(bytes, 0, bytes.Length);

        // Image count
        bytes = GetBytes(images.Count);
        await stream.WriteAsync(bytes, 0, bytes.Length);

        // Interval in ms
        bytes = GetBytes(interval);
        await stream.WriteAsync(bytes, 0, bytes.Length);

        // Reserved
        for (var i = 20; i <= 63; i++)
        {
            bytes = new byte[] { 0x00 };
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        // Write the bif index
        var index = 0;
        long imageOffset = 64 + (8 * images.Count) + 8;

        foreach (var img in images)
        {
            bytes = GetBytes(index);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            bytes = GetBytes(imageOffset);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            imageOffset += img.Length;

            index++;
        }

        bytes = new byte[] { 0xff, 0xff, 0xff, 0xff };
        await stream.WriteAsync(bytes, 0, bytes.Length);

        bytes = GetBytes(imageOffset);
        await stream.WriteAsync(bytes, 0, bytes.Length);

        // Write the images
        foreach (var img in images)
        {
            using (var imgStream = new FileStream(img.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                await imgStream.CopyToAsync(stream).ConfigureAwait(false);
            }
        }
    }

    /*
     * Utility Methods
     */
    private void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error deleting {0}: {1}", ex, directory);
        }
    }

    private byte[] GetBytes(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private byte[] GetBytes(long value)
    {
        var intVal = Convert.ToInt32(value);
        return GetBytes(intVal);
    }
}
