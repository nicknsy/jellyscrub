using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Nick.Plugin.Jellyscrub.Drawing;

namespace Nick.Plugin.Jellyscrub.Conversion;

/// <summary>
/// Shared task for conversion and deletion of BIF files. 
/// </summary>
public class ConversionTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;

    private readonly PrettyLittleLogger _convertLogger = new PrettyLittleLogger();
    private readonly PrettyLittleLogger _deleteLogger = new PrettyLittleLogger();

    private bool _busy = false;
    private readonly object _lock = new object();

    public ConversionTask(ILibraryManager libraryManager, IFileSystem fileSystem)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
    }

    public async void ConvertAll()
    {
        if (!CheckAndSetBusy(_convertLogger)) return;

        _convertLogger.ClearSynchronized();
        foreach (var convertInfo in await GetBifFiles())
        {
            _convertLogger.LogSynchronized($"Converting {convertInfo.Path}", PrettyLittleLogger.LogColor.Green);
        }

        _busy = false;
    }

    public void DeleteAll()
    {
        if (!CheckAndSetBusy(_deleteLogger)) return;

        _deleteLogger.ClearSynchronized();
        for (int i = 0; i < 200; i++)
        {
            Thread.Sleep(500);
            _deleteLogger.LogSynchronized($"Delete message {i}", PrettyLittleLogger.LogColor.Green);
        }

        _busy = false;
    }

    private async Task<List<ConvertInfo>> GetBifFiles()
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
            Manifest? manifest = await VideoProcessor.GetManifest(item, _fileSystem);
            if (manifest?.WidthResolutions == null) continue;

            foreach (var width in manifest.WidthResolutions)
            {
                var path = VideoProcessor.GetExistingBifPath(item, _fileSystem, width);
                if (path != null)
                {
                    bifFiles.Add(new ConvertInfo { Id = item.Id, Path = path, Width = width });
                }
            }
        }

        return bifFiles;
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
                logger.LogSynchronized("[!] Already busy running a task.", PrettyLittleLogger.LogColor.Red);
                return false;
            }
            _busy = true;
            return true;
        }
    }
}
