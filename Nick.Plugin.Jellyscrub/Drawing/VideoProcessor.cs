using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using System.Globalization;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Configuration;
using System.Text.Json;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Configuration;

namespace Nick.Plugin.Jellyscrub.Drawing;

public class VideoProcessor
{
    private readonly ILogger<VideoProcessor> _logger;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly PluginConfiguration _config;
    private readonly OldMediaEncoder _oldEncoder;

    public VideoProcessor(
        ILoggerFactory loggerFactory,
        ILogger<VideoProcessor> logger,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        EncodingHelper encodingHelper)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _config = JellyscrubPlugin.Instance!.Configuration;
        _oldEncoder = new OldMediaEncoder(loggerFactory.CreateLogger<OldMediaEncoder>(), mediaEncoder, configurationManager, fileSystem, encodingHelper);
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
                /*
                 * It seems that in Jellyfin multiple files in the same folder exist both as separate items
                 * and as sub-media sources under a single head item. Because of this, it is worth a simple check
                 * to make sure we are not writing a "sub-items" trickplay data to the metadata folder of the "main" item.
                 */
                if (!item.Id.Equals(Guid.Parse(mediaSource.Id))) continue;

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
                    await CreateManifest(item, width).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while creating BIF file");
            }
            finally
            {
                BifWriterSemaphore.Release();
            }
        }
    }

    /*
     * Methods for getting storage paths of Manifest files
     */
    private bool HasManifest(BaseItem item, IFileSystem fileSystem)
    {
        return !string.IsNullOrWhiteSpace(GetExistingManifestPath(item, fileSystem));
    }

    private static string GetNewManifestPath(BaseItem item)
    {
        return JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalManifestPath(item) : GetInternalManifestPath(item);
    }

    public static string? GetExistingManifestPath(BaseItem item, IFileSystem fileSystem)
    {
        var path = JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalManifestPath(item) : GetInternalManifestPath(item);

        return fileSystem.FileExists(path) ? path : null;
    }

    private static string GetLocalManifestPath(BaseItem item)
    {
        var folder = Path.Combine(item.ContainingFolderPath, "trickplay");
        var filename = Path.GetFileNameWithoutExtension(item.Path);
        filename += "-" + "manifest.json";

        return Path.Combine(folder, filename);
    }

    private static string GetInternalManifestPath(BaseItem item)
    {
        return Path.Combine(item.GetInternalMetadataPath(), "trickplay", "manifest.json");
    }

    /*
     * Manifest Creation
     */
    private async Task CreateManifest(BaseItem item, int width)
    {
        // Create Manifest object with new resolution
        Manifest newManifest = new Manifest() {
            Version = JellyscrubPlugin.Instance!.Version.ToString(),
            WidthResolutions = new[] { width }
        };

        // If a Manifest object already exists, combine resolutions
        var path = GetNewManifestPath(item);
        if (HasManifest(item, _fileSystem))
        {
            using FileStream openStream = File.OpenRead(path);
            Manifest? oldManifest = await JsonSerializer.DeserializeAsync<Manifest>(openStream);

            if (oldManifest != null && oldManifest.WidthResolutions != null)
            {
                newManifest.WidthResolutions = newManifest.WidthResolutions.Concat(oldManifest.WidthResolutions).ToArray();
            }
        }

        // Serialize and write to manifest file
        using FileStream createStream = File.Create(path);
        await JsonSerializer.SerializeAsync(createStream, newManifest);
        await createStream.DisposeAsync();
    }

    /*
     * Methods for getting storage paths of BIFs
     */
    private bool HasBif(BaseItem item, IFileSystem fileSystem, int width)
    {
        return !string.IsNullOrWhiteSpace(GetExistingBifPath(item, fileSystem, width));
    }

    public static string? GetExistingBifPath(BaseItem item, IFileSystem fileSystem, int width)
    {
        var path = JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalBifPath(item, width) : GetInternalBifPath(item, width);

        return fileSystem.FileExists(path) ? path : null;
    }

    private static string GetNewBifPath(BaseItem item, int width)
    {
        return JellyscrubPlugin.Instance!.Configuration.LocalMediaFolderSaving ? GetLocalBifPath(item, width) : GetInternalBifPath(item, width);
    }

    private static string GetLocalBifPath(BaseItem item, int width)
    {
        var folder = Path.Combine(item.ContainingFolderPath, "trickplay");
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
        _logger.LogInformation("Creating trickplay files at {0} width, for {1} [ID: {2}]", width, mediaSource.Path, item.Id);

        var protocol = mediaSource.Protocol;

        var tempDirectory = Path.Combine(_appPaths.TempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var videoStream = mediaSource.VideoStream;

            var inputPath = mediaSource.Path;

            await _oldEncoder.ExtractVideoImagesOnInterval(inputPath, mediaSource.Container, videoStream, mediaSource, mediaSource.Video3DFormat,
                    TimeSpan.FromMilliseconds(interval), tempDirectory, "img_", width, cancellationToken)
                    .ConfigureAwait(false);

            var images = _fileSystem.GetFiles(tempDirectory, new string[] { ".jpg" }, false, false)
                .Where(img => string.Equals(img.Extension, ".jpg", StringComparison.Ordinal))
                .OrderBy(i => i.FullName)
                .ToList();

            if (images.Count == 0) throw new InvalidOperationException("Cannot make BIF file from 0 images.");

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

                // Create .ignore file so trickplay folder is not picked up as a season when TV folder structure is improper.
                var ignorePath = Path.Combine(Directory.GetParent(path).FullName, ".ignore");
                if (!File.Exists(ignorePath)) await File.Create(ignorePath).DisposeAsync();

                _logger.LogInformation("Finished creation of trickplay file {0}", path);
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
