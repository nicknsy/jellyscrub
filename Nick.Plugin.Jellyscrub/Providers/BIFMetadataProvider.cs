using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Drawing;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Configuration;
using Nick.Plugin.Jellyscrub.Configuration;

namespace Nick.Plugin.Jellyscrub.Providers;

/// <summary>
/// Class BIFMetadataProvider. Doen't actually provide metadata but is used to
/// generate BIF files on library scan.
/// </summary>
public class BIFMetadataProvider : ICustomMetadataProvider<Episode>,
    ICustomMetadataProvider<MusicVideo>,
    ICustomMetadataProvider<Movie>,
    ICustomMetadataProvider<Video>,
    IHasItemChangeMonitor,
    IHasOrder,
    IForcedProvider
{
    private readonly ILogger<BIFMetadataProvider> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly EncodingHelper _encodingHelper;

    public BIFMetadataProvider(
        ILogger<BIFMetadataProvider> logger,
        ILoggerFactory loggerFactory,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        EncodingHelper encodingHelper)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _mediaEncoder = mediaEncoder;
        _configurationManager = configurationManager;
        _encodingHelper = encodingHelper;
    }

    /// <inheritdoc />
    public string Name => "Jellyscrub Trickplay Generator";

    /// <summary>
    /// Run after the core media info provider (which is 100)
    /// </summary>
    public int Order => 1000;

    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (item.IsFileProtocol)
        {
            var file = directoryService.GetFile(item.Path);
            if (file != null && item.DateModified != file.LastWriteTimeUtc)
            {
                return true;
            }
        }

        return false;
    }

    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchInternal(item, options, cancellationToken);
    }

    public Task<ItemUpdateType> FetchAsync(MusicVideo item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchInternal(item, options, cancellationToken);
    }

    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchInternal(item, options, cancellationToken);
    }

    public Task<ItemUpdateType> FetchAsync(Video item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchInternal(item, options, cancellationToken);
    }

    public static long MinRunTimeTicks = TimeSpan.FromSeconds(30).Ticks;

    public static bool EnableForItem(Video item, IFileSystem fileSystem)
    {
        var videoType = item.VideoType;

        if (videoType == VideoType.Iso)
        {
            return false;
        }

        if (videoType == VideoType.BluRay)
        {
            return false;
        }

        if (videoType == VideoType.Dvd)
        {
            return false;
        }

        if (item.IsShortcut)
        {
            return false;
        }

        if (!item.IsCompleteMedia)
        {
            return false;
        }

        if (!item.RunTimeTicks.HasValue || item.RunTimeTicks.Value < MinRunTimeTicks)
        {
            return false;
        }

        if (item.IsFileProtocol)
        {
            if (!fileSystem.FileExists(item.Path))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    private async Task<ItemUpdateType> FetchInternal(Video item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var config = JellyscrubPlugin.Instance!.Configuration;

        if (!EnableForItem(item, _fileSystem))
        {
            return ItemUpdateType.None;
        }

        if (config.ExtractionDuringLibraryScan)
        {
            var videoProcessor = new VideoProcessor(_loggerFactory, _loggerFactory.CreateLogger<VideoProcessor>(), _mediaEncoder, _configurationManager, _fileSystem, _appPaths, _libraryMonitor, _encodingHelper);

            switch (config.ScanBehavior)
            {
                case MetadataScanBehavior.Blocking:
                    await videoProcessor.Run(item, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                case MetadataScanBehavior.NonBlocking:
                    _ = videoProcessor.Run(item, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        // The core doesn't need to trigger any save operations over this
        return ItemUpdateType.None;
    }
}
