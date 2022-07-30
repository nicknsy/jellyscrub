using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Configuration;
using Nick.Plugin.Jellyscrub.Drawing;
using MediaBrowser.Model.Globalization;

namespace Nick.Plugin.Jellyscrub.ScheduledTasks;

/// <summary>
/// Class BIFGenerationTask.
/// </summary>
public class BIFGenerationTask : IScheduledTask
{
    private readonly ILogger<BIFGenerationTask> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly IApplicationPaths _appPaths;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly ILocalizationManager _localization;

    public BIFGenerationTask(
        ILibraryManager libraryManager,
        ILogger<BIFGenerationTask> logger,
        ILoggerFactory loggerFactory,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        IServerConfigurationManager configurationManager,
        ILocalizationManager localization)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _configurationManager = configurationManager;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Name => "Generate BIF Files";

    /// <inheritdoc />
    public string Key => "GenerateBIFFiles";

    /// <inheritdoc />
    public string Description => "Generates BIF files to be used for jellyscrub scrubbing preview.";

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromHours(5).Ticks
                }
            };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
            IsVirtualItem = false//,
            //MinRunTimeTicks = Providers.RokuMetadataProvider.MinRunTimeTicks

        }).OfType<Video>().ToList();

        var numComplete = 0;

        foreach (var item in items)
        {
            if (!Providers.BIFMetadataProvider.EnableForItem(item, _fileSystem))
            {
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await new VideoProcessor(_loggerFactory.CreateLogger<VideoProcessor>(), _fileSystem, _appPaths, _libraryMonitor, _loggerFactory, _configurationManager)
                    .Run(item, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error creating trickplay files for {0}: {1}", item.Name, ex);
            }

            numComplete++;
            double percent = numComplete;
            percent /= items.Count;
            percent *= 100;

            progress.Report(percent);
        }
    }
}
