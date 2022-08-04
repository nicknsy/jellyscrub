using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Drawing;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Configuration;

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
    private readonly ILocalizationManager _localization;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _configurationManager;

    public BIFGenerationTask(
        ILibraryManager libraryManager,
        ILogger<BIFGenerationTask> logger,
        ILoggerFactory loggerFactory,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        ILocalizationManager localization,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _fileSystem = fileSystem;
        _appPaths = appPaths;
        _libraryMonitor = libraryMonitor;
        _localization = localization;
        _mediaEncoder = mediaEncoder;
        _configurationManager = configurationManager;
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
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
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
            IsVirtualItem = false

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

                await new VideoProcessor(_loggerFactory, _loggerFactory.CreateLogger<VideoProcessor>(), _mediaEncoder, _configurationManager, _fileSystem, _appPaths, _libraryMonitor)
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
