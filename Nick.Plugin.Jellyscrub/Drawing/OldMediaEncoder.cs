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

    private readonly SemaphoreSlim _thumbnailResourcePool = new(1, 1);
    private readonly object _runningProcessesLock = new();
    private readonly List<ProcessWrapper> _runningProcesses = new();

    private string _ffmpegPath;
    private int _threads;
    private readonly PluginConfiguration _config;

    public OldMediaEncoder(
	    ILogger<OldMediaEncoder> logger,
	    IMediaEncoder mediaEncoder,
	    IServerConfigurationManager configurationManager,
	    IFileSystem fileSystem)
    {
        _logger = logger;
        _mediaEncoder = mediaEncoder;
        _fileSystem = fileSystem;
        _configurationManager = configurationManager;

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
        var inputArgument = _mediaEncoder.GetInputArgument(inputFile, mediaSource);

        var vf = "-filter:v fps=1/" + interval.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        var maxWidthParam = maxWidth.ToString(CultureInfo.InvariantCulture);

        vf += string.Format(CultureInfo.InvariantCulture, ",scale=min(iw\\,{0}):trunc(ow/dar/2)*2", maxWidthParam);

        // HDR Software Tonemapping
        if ((string.Equals(videoStream?.ColorTransfer, "smpte2084", StringComparison.OrdinalIgnoreCase)
            || string.Equals(videoStream?.ColorTransfer, "arib-std-b67", StringComparison.OrdinalIgnoreCase))
            && _mediaEncoder.SupportsFilter("zscale"))
        {
            vf += ",zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0:peak=100,zscale=t=bt709:m=bt709,format=yuv420p";
        }

        Directory.CreateDirectory(targetDirectory);
        var outputPath = Path.Combine(targetDirectory, filenamePrefix + "%08d.jpg");

        var args = string.Format(CultureInfo.InvariantCulture, "-i {0} -threads {3} -v quiet {2} -f image2 \"{1}\"", inputArgument, outputPath, vf, _threads);

        if (!string.IsNullOrWhiteSpace(container))
        {
            var inputFormat = EncodingHelper.GetInputFormat(container);
            if (!string.IsNullOrWhiteSpace(inputFormat))
            {
                args = "-f " + inputFormat + " " + args;
            }
        }

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
                var msg = string.Format(CultureInfo.InvariantCulture, "ffmpeg image extraction failed for {0}", inputArgument);

                _logger.LogError(msg);

                throw new FfmpegException(msg);
            }
        }
    }

    private void StartProcess(ProcessWrapper process)
    {
        process.Process.Start();
        try
        {
            process.Process.PriorityClass = _config.ProcessPriority;
        }
        catch (Exception e)
        {
            _logger.LogError("Unable to set process priority: {0}", e.Message);
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

            _logger.LogInformation("Killing ffmpeg process");

            process.Process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The process has already exited or
            // there is no process associated with this Process object.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process");
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
