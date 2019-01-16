using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using RokuMetadata.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Entities;
using System;

namespace RokuMetadata.Providers
{
    public class RokuMetadataProvider : ICustomMetadataProvider<Episode>,
        ICustomMetadataProvider<MusicVideo>,
        ICustomMetadataProvider<Movie>,
        ICustomMetadataProvider<Video>,
        IHasItemChangeMonitor,
        IHasOrder,
        IForcedProvider
    {
        private readonly ILogger _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly ILibraryMonitor _libraryMonitor;

        public RokuMetadataProvider(ILogger logger, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IApplicationPaths appPaths, ILibraryMonitor libraryMonitor)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _libraryMonitor = libraryMonitor;
        }

        public string Name
        {
            get { return Plugin.PluginName; }
        }
        
        public int Order
        {
            get
            {
                // Run after the core media info provider (which is 100)
                return 1000;
            }
        }

        public bool HasChanged(BaseItem item, IDirectoryService directoryService)
        {
            if (item.IsFileProtocol)
            {
                var file = directoryService.GetFile(item.Path);
                if (file != null && item.HasDateModifiedChanged(file.LastWriteTimeUtc))
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
            var container = item.Container;

            if (string.Equals(container, MediaContainer.Iso, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(container, MediaContainer.Bluray, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.Equals(container, MediaContainer.Dvd, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(container, MediaContainer.BlurayIso, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (string.Equals(container, MediaContainer.DvdIso, StringComparison.OrdinalIgnoreCase))
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
            if (!EnableForItem(item, _fileSystem))
            {
                return ItemUpdateType.None;
            }

            if (Plugin.Instance.Configuration.EnableExtractionDuringLibraryScan)
            {
                await new VideoProcessor(_logger, _mediaEncoder, _fileSystem, _appPaths, _libraryMonitor)
                    .Run(item, cancellationToken).ConfigureAwait(false);
            }

            // The core doesn't need to trigger any save operations over this
            return ItemUpdateType.None;
        }
    }
}
