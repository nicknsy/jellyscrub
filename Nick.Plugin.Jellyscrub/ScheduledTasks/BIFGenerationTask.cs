using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Nick.Plugin.Jellyscrub.Drawing;

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
    private readonly EncodingHelper _encodingHelper;
    private readonly ManualResetEvent _waitHandleParallelProcesses;
    private readonly ManualResetEvent _waitHandleReportProgress;
    private int _currentExecutionIndex;
    private int _numComplete = 0;

    public BIFGenerationTask(
        ILibraryManager libraryManager,
        ILogger<BIFGenerationTask> logger,
        ILoggerFactory loggerFactory,
        IFileSystem fileSystem,
        IApplicationPaths appPaths,
        ILibraryMonitor libraryMonitor,
        ILocalizationManager localization,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager configurationManager,
        EncodingHelper encodingHelper)
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
        _encodingHelper = encodingHelper;
        _waitHandleParallelProcesses = new ManualResetEvent(true);
        _waitHandleReportProgress = new ManualResetEvent(true);
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
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            MediaTypes = new[] { MediaType.Video },
            IsVirtualItem = false,
            Recursive = true

        }).OfType<Video>().ToList();

        var config = JellyscrubPlugin.Instance!.Configuration;
        int parallelProcessesCount = config.ParallelProcesses > 0 ? config.ParallelProcesses : 1;

        _currentExecutionIndex = -1;
        _numComplete = 0;

        var parallelProcessArray = new Task[parallelProcessesCount];

        for (var i = 0; i < parallelProcessesCount; i++)
        {
            parallelProcessArray[i] = Task.Run(async () =>
            {
                int executionIndex = 0;
                while (!cancellationToken.IsCancellationRequested && executionIndex < items.Count)
                {
                    _waitHandleParallelProcesses.WaitOne();

                    _currentExecutionIndex++;
                    executionIndex = _currentExecutionIndex;

                    _waitHandleParallelProcesses.Set();

                    if (executionIndex < items.Count)
                    {
                        var item = items[executionIndex];

                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            await new VideoProcessor(_loggerFactory, _loggerFactory.CreateLogger<VideoProcessor>(), _mediaEncoder, _configurationManager, _fileSystem, _appPaths, _libraryMonitor, _encodingHelper)
                                .Run(item, true, cancellationToken).ConfigureAwait(false);

                            ReportProgress(progress, items.Count);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError("Error creating trickplay files for {0}: {1}", item.Name, ex);
                        }
                    }
                }
            }, cancellationToken);
        }

        foreach (var task in parallelProcessArray)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        _logger.LogInformation("Creating Trickplay Task Finished");
    }

    /// <summary>
    /// Report the current progress of the task.  
    /// </summary>
    public void ReportProgress(IProgress<double> progress, int maxCount)
    {
        _waitHandleReportProgress.WaitOne();

        _numComplete++;
        double percent = _numComplete;
        percent /= maxCount;
        percent *= 100;
        progress.Report(percent);

        _waitHandleReportProgress.Set();
    }
}
