using System.Globalization;
using System.Text;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Drawing;

namespace Nick.Plugin.Jellyscrub.Conversion;

/// <summary>
/// Shared task for conversion and deletion of BIF files. 
/// </summary>
public class ConversionTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ITrickplayManager _trickplayManager;
    private readonly IApplicationPaths _appPaths;
    private readonly IServerConfigurationManager _configManager;
    private readonly ILogger _logger;

    private readonly PrettyLittleLogger _convertLogger = new PrettyLittleLogger();
    private readonly PrettyLittleLogger _deleteLogger = new PrettyLittleLogger();

    private bool _busy = false;
    private readonly object _lock = new object();

    public ConversionTask(
        ILibraryManager libraryManager,
        IFileSystem fileSystem,
        ITrickplayManager trickplayManager,
        IApplicationPaths appPaths,
        IServerConfigurationManager configManager,
        ILogger<ConversionTask> logger
        )
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _trickplayManager = trickplayManager;
        _appPaths = appPaths;
        _configManager = configManager;
        _logger = logger;
    }

    /*
     * 
     * Conversion
     * 
     */
    public async Task ConvertAll(bool forceConvert)
    {
        if (!CheckAndSetBusy(_convertLogger)) return;

        _convertLogger.ClearSynchronized();

        int attempted = 0;
        int completed = 0;
        foreach (var convertInfo in await GetConvertInfo().ConfigureAwait(false))
        {
            try
            {
                // Check that it doesn't already exist
                var tilesMetaDir = GetTrickplayDirectory(convertInfo.Item, convertInfo.Width);
                var itemId = convertInfo.Item.Id;
                var width = convertInfo.Width;
                var bifPath = convertInfo.Path;

                if (!forceConvert && Directory.Exists(tilesMetaDir) && (await _trickplayManager.GetTrickplayResolutions(itemId).ConfigureAwait(false)).ContainsKey(width))
                {
                    _convertLogger.LogSynchronized($"Found existing trickplay files for {bifPath}, use force re-convert if necessary. Skipping...", PrettyLittleLogger.LogColor.Info);
                    continue;
                }

                // Extract images
                attempted++;
                _convertLogger.LogSynchronized($"Converting {bifPath}", PrettyLittleLogger.LogColor.Info);

                var imgTempDir = Path.Combine(_appPaths.TempDirectory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(imgTempDir);
                var images = await ExtractImages(bifPath, imgTempDir);

                if (images.Count == 0)
                {
                    _convertLogger.LogSynchronized($"Image extration for {bifPath} returned an empty list. Skipping...", PrettyLittleLogger.LogColor.Error);
                    continue;
                }

                // Create tiles
                Directory.CreateDirectory(tilesMetaDir);
                TrickplayInfo trickplayInfo = _trickplayManager.CreateTiles(images, convertInfo.Width, _configManager.Configuration.TrickplayOptions, tilesMetaDir);

                // Save trickplay info
                trickplayInfo.ItemId = itemId;
                await _trickplayManager.SaveTrickplayInfo(trickplayInfo).ConfigureAwait(false);

                // Delete temp folder
                Directory.Delete(imgTempDir, true);

                _convertLogger.LogSynchronized($"Finished converting {bifPath}", PrettyLittleLogger.LogColor.Sucess);
                completed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting BIF file {0}", convertInfo.Path);
                _convertLogger.LogSynchronized($"Encountered error while converting {convertInfo.Path}, please check the console.", PrettyLittleLogger.LogColor.Error);
            }
        }

        if (attempted > 0)
            _convertLogger.LogSynchronized($"Successfully converted {completed}/{attempted} .BIF files!", PrettyLittleLogger.LogColor.Info);

        _busy = false;
    }

    private async Task<List<string>> ExtractImages(string bifPath, string outputDir)
    {
        List<string> images = new();
        List<UInt32> imageOffsets = new();

        using var bifStream = File.OpenRead(bifPath);
        using var bifReader = new BinaryReader(bifStream);

        // Skip to index section
        bifStream.Seek(64, SeekOrigin.Begin);
        UInt32 index = 0;
        while (index < UInt32.MaxValue)
        {
            var timestamp = bifReader.ReadUInt32();
            var offset = bifReader.ReadUInt32();
            imageOffsets.Add(offset);

            if (timestamp == UInt32.MaxValue)
                break;
        }

        // Bif files must be adjacent, so only seek once to first image
        if (imageOffsets.Count > 1)
            bifStream.Seek(imageOffsets[0], SeekOrigin.Begin);

        // Extract images
        _logger.LogInformation("Extracting BIF images to {0}", outputDir);
        for (int i = 0; i < imageOffsets.Count - 1; i++)
        {
            var length = imageOffsets[i + 1] - imageOffsets[i];

            var imgPath = Path.Combine(outputDir, i.ToString(CultureInfo.InvariantCulture) + ".jpg");
            using var imgStream = File.Create(imgPath);
            byte[] imgBytes = new byte[length];
            await bifStream.ReadExactlyAsync(imgBytes, 0, (int)length).ConfigureAwait(false);
            await imgStream.WriteAsync(imgBytes).ConfigureAwait(false);

            images.Add(imgPath);
        }

        return images;
    }

    /*
     * 
     * Deletion
     * 
     */
    public async Task DeleteAll()
    {
        if (!CheckAndSetBusy(_deleteLogger)) return;

        _deleteLogger.ClearSynchronized();
        foreach (var convertInfo in await GetConvertInfo().ConfigureAwait(false))
        {
            _convertLogger.LogSynchronized($"Deleting {convertInfo.Path}", PrettyLittleLogger.LogColor.Info);
        }

        _busy = false;
    }

    /*
     * 
     * Util
     * 
     */
    private async Task<List<ConvertInfo>> GetConvertInfo()
    {
        List<ConvertInfo> bifFiles = new();

        // Get all items
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
            IsVirtualItem = false,
            Recursive = true

        }).OfType<Video>().ToList();

        // Get all BIF files and widths
        foreach (var item in items)
        {
            try
            {
                Manifest? manifest = await VideoProcessor.GetManifest(item, _fileSystem);
                if (manifest?.WidthResolutions == null) continue;

                foreach (var width in manifest.WidthResolutions)
                {
                    var path = VideoProcessor.GetExistingBifPath(item, _fileSystem, width);
                    if (path != null)
                    {
                        bifFiles.Add(new ConvertInfo { Item = item, Path = path, Width = width });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading manifest for item \"{0}\" ({1})", item.Name, item.Id);
            }
        }

        return bifFiles;
    }

    private string GetTrickplayDirectory(BaseItem item, int? width = null)
    {
        var path = Path.Combine(item.GetInternalMetadataPath(), "trickplay");

        return width.HasValue ? Path.Combine(path, width.Value.ToString(CultureInfo.InvariantCulture)) : path;
    }

    public string GetConvertLog()
    {
        return _convertLogger.ReadSynchronized();
    }

    public string GetDeleteLog()
    {
        return _deleteLogger.ReadSynchronized();
    }

    private bool CheckAndSetBusy(PrettyLittleLogger logger)
    {
        lock (_lock)
        {
            if (_busy)
            {
                logger.LogSynchronized("[!] Already busy running a task.", PrettyLittleLogger.LogColor.Error);
                return false;
            }
            _busy = true;
            return true;
        }
    }
}
