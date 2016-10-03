using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.IO;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using RokuMetadata.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonIO;

namespace RokuMetadata.Drawing
{
    public class VideoProcessor
    {
        private readonly ILogger _logger;
        private readonly IMediaEncoder _mediaEncoder;
        private readonly IFileSystem _fileSystem;
        private readonly IApplicationPaths _appPaths;
        private readonly ILibraryMonitor _libraryMonitor;

        public VideoProcessor(ILogger logger, IMediaEncoder mediaEncoder, IFileSystem fileSystem, IApplicationPaths appPaths, ILibraryMonitor libraryMonitor)
        {
            _logger = logger;
            _mediaEncoder = mediaEncoder;
            _fileSystem = fileSystem;
            _appPaths = appPaths;
            _libraryMonitor = libraryMonitor;
        }

        public async Task Run(Video item, CancellationToken cancellationToken)
        {
            var mediaSources = ((IHasMediaSources)item).GetMediaSources(false)
                .ToList();

            var modifier = GetItemModifier(item);

            foreach (var mediaSource in mediaSources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Plugin.Instance.Configuration.EnableHdThumbnails)
                {
                    await Run(item, modifier, mediaSource, 320, cancellationToken).ConfigureAwait(false);
                }
                if (Plugin.Instance.Configuration.EnableSdThumbnails)
                {
                    await Run(item, modifier, mediaSource, 240, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task Run(Video item, string itemModifier, MediaSourceInfo mediaSource, int width, CancellationToken cancellationToken)
        {
            if (!HasBif(item, itemModifier, width, mediaSource))
            {
                await BifWriterSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    if (!HasBif(item, itemModifier, width, mediaSource))
                    {
                        await CreateBif(item, itemModifier, width, mediaSource, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    BifWriterSemaphore.Release();
                }
            }
        }

        private bool HasBif(Video item, string itemModifier, int width, MediaSourceInfo mediaSource)
        {
            return !string.IsNullOrWhiteSpace(GetExistingBifPath(item, itemModifier, mediaSource.Id, width));
        }

        public static string GetItemModifier(BaseItem item)
        {
            return item.DateModified.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        public static string GetExistingBifPath(BaseItem item, string mediaSourceId, int width)
        {
            return GetExistingBifPath(item, GetItemModifier(item), mediaSourceId, width);
        }

        private static string GetExistingBifPath(BaseItem item, string itemModifier, string mediaSourceId, int width)
        {
            var path = GetLocalBifPath(item, width);

            if (File.Exists(path))
            {
                return path;
            }

            path = GetInternalBifPath(item, itemModifier, mediaSourceId, width);

            if (File.Exists(path))
            {
                return path;
            }

            return null;
        }

        private static string GetNewBifPath(BaseItem item, string itemModifier, string mediaSourceId, int width)
        {
            if (Plugin.Instance.Configuration.EnableLocalMediaFolderSaving)
            {
                return GetLocalBifPath(item, width);
            }

            return GetInternalBifPath(item, itemModifier, mediaSourceId, width);
        }

        private static string GetLocalBifPath(BaseItem item, int width)
        {
            var folder = item.ContainingFolderPath;
            var filename = Path.GetFileNameWithoutExtension(item.Path);
            filename += "-" + width.ToString(CultureInfo.InvariantCulture) + ".bif";

            return Path.Combine(folder, filename);
        }

        private static string GetInternalBifPath(BaseItem item, string modifier, string mediaSourceId, int width)
        {
            return Path.Combine(item.GetInternalMetadataPath(), "bif", modifier, mediaSourceId, width.ToString(CultureInfo.InvariantCulture), "index.bif");
        }

        private Task CreateBif(Video item, string itemModifier, int width, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
        {
            var path = GetNewBifPath(item, itemModifier, mediaSource.Id, width);

            return CreateBif(path, width, mediaSource, cancellationToken);
        }

        private async Task CreateBif(string path, int width, MediaSourceInfo mediaSource, CancellationToken cancellationToken)
        {
            _logger.Info("Creating roku thumbnails at {0} width, for {1}", width, mediaSource.Path);

            var protocol = mediaSource.Protocol;

            var inputPath = MediaEncoderHelpers.GetInputArgument(_fileSystem, mediaSource.Path, protocol, null, mediaSource.PlayableStreamFileNames);

            var tempDirectory = Path.Combine(_appPaths.TempDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                await _mediaEncoder.ExtractVideoImagesOnInterval(inputPath, protocol, mediaSource.Video3DFormat,
                        TimeSpan.FromSeconds(10), tempDirectory, "img_", width, cancellationToken)
                        .ConfigureAwait(false);

                var images = new DirectoryInfo(tempDirectory)
                    .EnumerateFiles()
                    .Where(img => string.Equals(img.Extension, ".jpg", StringComparison.Ordinal))
                    .OrderBy(i => i.FullName)
                    .ToList();

                var bifTempPath = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N"));

                using (var fs = _fileSystem.GetFileStream(bifTempPath, FileMode.Create, FileAccess.Write, FileShare.Read, true))
                {
                    await CreateBif(fs, images).ConfigureAwait(false);
                }

                _libraryMonitor.ReportFileSystemChangeBeginning(path);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(bifTempPath, path, true);
                }
                finally
                {
                    _libraryMonitor.ReportFileSystemChangeComplete(path, false);
                }
            }
            finally
            {
                DeleteDirectory(tempDirectory);
            }
        }

        private static readonly SemaphoreSlim BifWriterSemaphore = new SemaphoreSlim(1, 1);
        public async Task<string> GetEmptyBif()
        {
            var path = Path.Combine(_appPaths.CachePath, "roku-thumbs", "empty.bif");

            if (!File.Exists(path))
            {
                await BifWriterSemaphore.WaitAsync().ConfigureAwait(false);

                try
                {
                    if (!File.Exists(path))
                    {
                        using (var fs = _fileSystem.GetFileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, true))
                        {
                            await CreateBif(fs, new List<FileInfo>()).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    BifWriterSemaphore.Release();
                }
            }

            return path;
        }

        public async Task CreateBif(Stream stream, List<FileInfo> images)
        {
            var magicNumber = new byte[] { 0x89, 0x42, 0x49, 0x46, 0x0d, 0x0a, 0x1a, 0x0a };
            await stream.WriteAsync(magicNumber, 0, magicNumber.Length);

            // version
            var bytes = GetBytes(0);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            // image count
            bytes = GetBytes(images.Count);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            // interval in ms
            bytes = GetBytes(10000);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            // reserved
            for (var i = 20; i <= 63; i++)
            {
                bytes = new byte[] { 0x00 };
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }

            // write the bif index
            var index = 0;
            long imageOffset = 64 + (8 * images.Count) + 8;

            foreach (var img in images)
            {
                bytes = GetBytes(index);
                await stream.WriteAsync(bytes, 0, bytes.Length);

                bytes = GetBytes(imageOffset);
                await stream.WriteAsync(bytes, 0, bytes.Length);

                imageOffset += img.Length;

                index++;
            }

            bytes = new byte[] { 0xff, 0xff, 0xff, 0xff };
            await stream.WriteAsync(bytes, 0, bytes.Length);

            bytes = GetBytes(imageOffset);
            await stream.WriteAsync(bytes, 0, bytes.Length);

            // write the images
            foreach (var img in images)
            {
                using (var imgStream = _fileSystem.GetFileStream(img.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, true))
                {
                    await imgStream.CopyToAsync(stream).ConfigureAwait(false);
                }
            }
        }

        private void DeleteDirectory(string directory)
        {
            try
            {
                _fileSystem.DeleteDirectory(directory, true);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error deleting {0}", ex, directory);
            }
        }

        private byte[] GetBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        private byte[] GetBytes(long value)
        {
            var intVal = Convert.ToInt32(value);
            return GetBytes(intVal);

            //byte[] bytes = BitConverter.GetBytes(value);
            //if (BitConverter.IsLittleEndian)
            //    Array.Reverse(bytes);
            //return bytes;
        }
    }
}
