using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Logging;
using RokuMetadata.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;

namespace RokuMetadata.ScheduledTasks
{
    public class RokuScheduledTask : IScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly ILibraryMonitor _libraryMonitor;

        public RokuScheduledTask(ILibraryManager libraryManager, ILogger logger, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IApplicationPaths appPaths, ILibraryMonitor libraryMonitor)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _libraryMonitor = libraryMonitor;
        }

        public string Category
        {
            get { return "Roku"; }
        }

        public string Key
        {
            get { return "RokuBif"; }
        }

        public string Description
        {
            get { return "Create thumbnails for enhanced seeking with Roku"; }
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var items = _libraryManager.RootFolder
                .RecursiveChildren
                .OfType<Video>()
                .ToList();

            var numComplete = 0;

            foreach (var item in items)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await new VideoProcessor(_logger, _mediaEncoder, _fileSystem, _appPaths, _libraryMonitor)
                        .Run(item, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error creating roku thumbnails for {0}", ex, item.Name);
                }

                numComplete++;
                double percent = numComplete;
                percent /= items.Count;
                percent *= 100;

                progress.Report(percent);
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
                {
                    new TaskTriggerInfo
                    {
                        Type = TaskTriggerInfo.TriggerDaily,
                        TimeOfDayTicks = TimeSpan.FromHours(5).Ticks,
                        MaxRuntimeTicks = TimeSpan.FromHours(3).Ticks
                    }
                };
        }

        public string Name
        {
            get { return "Create thumbnails"; }
        }
    }
}
