using MediaBrowser.Common;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.IO;
using Nick.Plugin.Jellyscrub.Configuration;
using MediaBrowser.Model.Configuration;

namespace Nick.Plugin.Jellyscrub.Drawing;

/// <summary>
/// Re-implementation of removed MediaEncoder methods.
/// </summary>
public class OldMediaEncoder
{
    private readonly ILogger _logger;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IFileSystem _fileSystem;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly EncodingHelper _encodingHelper;

    private readonly SemaphoreSlim _thumbnailResourcePool = new(1, 1);
    private readonly object _runningProcessesLock = new();
    private readonly List<ProcessWrapper> _runningProcesses = new();

    private readonly PluginConfiguration _config;
    private string _ffmpegPath;
    private int _threads;
    private bool _doHwAcceleration;
    private bool _doHwEncode;

    public OldMediaEncoder(
	    ILogger<OldMediaEncoder> logger,
	    IMediaEncoder mediaEncoder,
	    IServerConfigurationManager configurationManager,
	    IFileSystem fileSystem,
        EncodingHelper encodingHelper)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        _fileSystem = fileSystem;
        _configurationManager = configurationManager;
        _encodingHelper = encodingHelper;

        _config = JellyscrubPlugin.Instance!.Configuration;
        var configThreads = _config.ProcessThreads;

        var encodingConfig = _configurationManager.GetEncodingOptions();
        _ffmpegPath = _mediaEncoder.EncoderPath;

        if (string.IsNullOrWhiteSpace(_ffmpegPath))
        {
            _mediaEncoder.SetFFmpegPath();
            _ffmpegPath = _mediaEncoder.EncoderPath;
        }

        _threads = configThreads == -1 ? EncodingHelper.GetNumberOfThreads(null, encodingConfig, null) : configThreads;

        var hwAcceleration = _config.HwAcceleration;
        _doHwAcceleration = (hwAcceleration != HwAccelerationOptions.None);
        _doHwEncode = (hwAcceleration == HwAccelerationOptions.Full);
    }

    public async Task ExtractVideoImagesOnInterval(
            string inputFile,
            string container,
            MediaStream videoStream,
            MediaSourceInfo mediaSource,
            Video3DFormat? threedFormat,
            TimeSpan interval,
            string targetDirectory,
            string filenamePrefix,
            int maxWidth,
            CancellationToken cancellationToken)
    {
        var options = _doHwAcceleration ? _configurationManager.GetEncodingOptions() : new EncodingOptions();

        // A new EncodingOptions instance must be used as to not disable HW acceleration for all of Jellyfin.
        // Additionally, we must set a few fields without defaults to prevent null pointer exceptions.
        if (!_doHwAcceleration)
        {
            options.EnableHardwareEncoding = false;
            options.HardwareAccelerationType = string.Empty;
            options.EnableTonemapping = false;
        }

        var hwAccelType = options.HardwareAccelerationType;

        var baseRequest = new BaseEncodingJobOptions { MaxWidth = maxWidth };
        var jobState = new EncodingJobInfo(TranscodingJobType.Progressive)
        {
            IsVideoRequest = true,  // must be true for InputVideoHwaccelArgs to return non-empty value
            MediaSource = mediaSource,
            VideoStream = videoStream,
            BaseRequest = baseRequest,  // GetVideoProcessingFilterParam errors if null
            MediaPath = inputFile,
            OutputVideoCodec = GetOutputCodec(hwAccelType)
        };

        // Get input and filter arguments
        var inputArgs = _encodingHelper.GetInputArgument(jobState, options, null).Trim();
        if (string.IsNullOrWhiteSpace(inputArgs)) throw new InvalidOperationException("EncodingHelper returned empty input arguments.");

        if (!_doHwAcceleration) inputArgs = "-threads " + _threads + " " + inputArgs; // HW accel args set a different input thread count, only set if disabled

        var filterParams = _encodingHelper.GetVideoProcessingFilterParam(jobState, options, jobState.OutputVideoCodec).Trim();
        if (string.IsNullOrWhiteSpace(filterParams) || filterParams.IndexOf("\"") == -1) throw new InvalidOperationException("EncodingHelper returned empty or invalid filter parameters.");

        filterParams = filterParams.Insert(filterParams.IndexOf("\"") + 1, "fps=1/" + interval.TotalSeconds.ToString(CultureInfo.InvariantCulture) + ","); // set framerate

        // Output arguments
        Directory.CreateDirectory(targetDirectory);
        var outputPath = Path.Combine(targetDirectory, filenamePrefix + "%08d.jpg");

        // Final command arguments
        var args = string.Format(
            CultureInfo.InvariantCulture,
            "-loglevel error {0} -an -sn {1} -threads {2} -c:v {3} -f {4} \"{5}\"",
            inputArgs,
            filterParams,
            _threads,
            jobState.OutputVideoCodec,
            "image2",
            outputPath);

        // Start ffmpeg process
        var processStartInfo = new ProcessStartInfo
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            FileName = _ffmpegPath,
            Arguments = args,
            WindowStyle = ProcessWindowStyle.Hidden,
            ErrorDialog = false
        };

        _logger.LogInformation(processStartInfo.FileName + " " + processStartInfo.Arguments);

        await _thumbnailResourcePool.WaitAsync(cancellationToken).ConfigureAwait(false);

        bool ranToCompletion = false;

        var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };
        using (var processWrapper = new ProcessWrapper(process, this))
        {
            try
            {
                StartProcess(processWrapper);

                // Need to give ffmpeg enough time to make all the thumbnails, which could be a while,
                // but we still need to detect if the process hangs.
                // Making the assumption that as long as new jpegs are showing up, everything is good.

                bool isResponsive = true;
                int lastCount = 0;

                while (isResponsive)
                {
                    if (await process.WaitForExitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
                    {
                        ranToCompletion = true;
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var jpegCount = _fileSystem.GetFilePaths(targetDirectory)
                        .Count(i => string.Equals(Path.GetExtension(i), ".jpg", StringComparison.OrdinalIgnoreCase));

                    isResponsive = jpegCount > lastCount;
                    lastCount = jpegCount;
                }

                if (!ranToCompletion)
                {
                    _logger.LogInformation("Killing ffmpeg process due to inactivity.");
                    StopProcess(processWrapper, 1000);
                }
            }
            finally
            {
                _thumbnailResourcePool.Release();
            }

            var exitCode = ranToCompletion ? processWrapper.ExitCode ?? 0 : -1;

            if (exitCode == -1)
            {
                var msg = string.Format(CultureInfo.InvariantCulture, "ffmpeg image extraction failed for {0}", inputFile);

                _logger.LogError(msg);

                throw new FfmpegException(msg);
            }
        }
    }

    public string GetOutputCodec(string hwaccelType)
    {
        if (_doHwAcceleration && _doHwEncode)
        {
            switch (hwaccelType.ToLower())
            {
                case "vaapi":
                    return "mjpeg_vaapi";
                case "qsv":
                    return "mjpeg_qsv";
            }
        }

        return "mjpeg";
    }

    private void StartProcess(ProcessWrapper process)
    {
        process.Process.Start();
        try
        {
            _logger.LogInformation("Setting generation process priority to {0}", _config.ProcessPriority);
            process.Process.PriorityClass = _config.ProcessPriority;
        }
        catch (Exception e)
        {
            _logger.LogError("Unable to set process priority: {0} (will not prevent BIF generation!)", e.Message);
        }

        lock (_runningProcessesLock)
        {
            _runningProcesses.Add(process);
        }
    }

    private void StopProcess(ProcessWrapper process, int waitTimeMs)
    {
        try
        {
            if (process.Process.WaitForExit(waitTimeMs))
            {
                return;
            }

            _logger.LogInformation("Killing process \"{0}\"", process.Process.ProcessName);

            process.Process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The process has already exited or
            // there is no process associated with this Process object.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process \"{0}\"", process.Process.ProcessName);
        }
    }

    private class ProcessWrapper : IDisposable
    {
        private readonly OldMediaEncoder _oldMediaEncoder;
        private bool _disposed = false;

        public ProcessWrapper(Process process, OldMediaEncoder oldMediaEncoder)
        {
            Process = process;
            _oldMediaEncoder = oldMediaEncoder;
            Process.Exited += OnProcessExited;
        }

        public Process Process { get; }

        public bool HasExited { get; private set; }

        public int? ExitCode { get; private set; }

        private void OnProcessExited(object sender, EventArgs e)
        {
            var process = (Process)sender;

            HasExited = true;

            try
            {
                ExitCode = process.ExitCode;
            }
            catch
            {
            }

            DisposeProcess(process);
        }

        private void DisposeProcess(Process process)
        {
            lock (_oldMediaEncoder._runningProcessesLock)
            {
                _oldMediaEncoder._runningProcesses.Remove(this);
            }

            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (Process != null)
                {
                    Process.Exited -= OnProcessExited;
                    DisposeProcess(Process);
                }
            }

            _disposed = true;
        }
    }
}
