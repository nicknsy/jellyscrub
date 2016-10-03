using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.IO;
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
using CommonIO;

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

        public bool HasChanged(IHasMetadata item, IDirectoryService directoryService)
        {
            if (item.EnableRefreshOnDateModifiedChange && !string.IsNullOrWhiteSpace(item.Path))
            {
                var file = directoryService.GetFile(item.Path);
                if (file != null && file.LastWriteTimeUtc != item.DateModified)
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

        public Task<ItemUpdateType> FetchAsync(LiveTvVideoRecording item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            return FetchInternal(item, options, cancellationToken);
        }

        public Task<ItemUpdateType> FetchAsync(Video item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            return FetchInternal(item, options, cancellationToken);
        }

        private async Task<ItemUpdateType> FetchInternal(Video item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
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
