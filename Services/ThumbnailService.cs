using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FTP_WebApp.Models;
using ImageMagick;
using Microsoft.Extensions.Options;

namespace FTP_WebApp.Services;

public class ThumbnailService : IThumbnailService
{
    private static readonly int[] AllowedWidths = [160, 320, 640, 960, 1600];
    private static readonly HashSet<string> BrowserImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".avif" };
    private readonly IRemoteFileService _remoteFiles;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly MediaOptions _options;
    private readonly string _cachePath;
    private readonly string? _ffmpegPath;
    private readonly ThumbnailGenerationCoordinator _coordinator;

    public ThumbnailService(IRemoteFileService remoteFiles, IWebHostEnvironment environment,
        IOptions<MediaOptions> options, ILogger<ThumbnailService> logger, ThumbnailGenerationCoordinator coordinator,
        IStoragePathProvider storagePaths)
    {
        _remoteFiles = remoteFiles;
        _logger = logger;
        _options = options.Value;
        _coordinator = coordinator;
        _cachePath = storagePaths.ResolvePath(_options.ThumbnailCachePath);
        _ffmpegPath = ResolveExecutable(_options.FfmpegPath, environment.ContentRootPath);
        Directory.CreateDirectory(_cachePath);

        if (_ffmpegPath is null)
        {
            _logger.LogWarning("Video thumbnails are disabled because FFmpeg was not found. Configure Media:FfmpegPath or " +
                "EAGLEFILES_FFMPEG_PATH with the full path to ffmpeg.exe.");
        }
        else
        {
            _logger.LogInformation("Video thumbnail generation is enabled with FFmpeg at {FfmpegPath}.", _ffmpegPath);
        }
    }

    public async Task<ThumbnailFile?> GetAsync(FtpConnectionProfile profile, string path, int width, string? version,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path);
        var category = FileTypeCatalog.GetCategory(extension);
        if (category is not ("image" or "video"))
        {
            return null;
        }
        if (category == "video" && _ffmpegPath is null)
        {
            return null;
        }

        var fullPreview = category == "image" && width <= 0;
        var browserOriginal = category == "image" && BrowserImageExtensions.Contains(extension);
        var originalPreview = fullPreview && browserOriginal;
        var normalizedWidth = fullPreview ? 0 : AllowedWidths.OrderBy(candidate => Math.Abs(candidate - width)).First();
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"preview-v3|{_options.ThumbnailQuality}|{_options.PreviewQuality}|{profile.Id}|{path}|{version}")))
            .ToLowerInvariant();
        var fullPreviewPath = Path.Combine(_cachePath, $"{cacheKey}-full{(browserOriginal ? ".original" : ".jpg")}");
        var cachedPath = fullPreview
            ? fullPreviewPath
            : Path.Combine(_cachePath, $"{cacheKey}-{normalizedWidth}.jpg");
        var etag = $"\"{cacheKey}-{(fullPreview ? "full" : normalizedWidth)}\"";
        var contentType = fullPreview && originalPreview ? GetBrowserImageContentType(extension) : "image/jpeg";
        if (File.Exists(cachedPath))
        {
            return new ThumbnailFile(cachedPath, contentType, etag);
        }

        var itemSlot = _coordinator.GetItemSlot(cacheKey);
        await itemSlot.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cachedPath))
            {
                return new ThumbnailFile(cachedPath, contentType, etag);
            }

            await _coordinator.Slots.WaitAsync(cancellationToken);
            try
            {
                var temporaryOutput = Path.Combine(_cachePath, $".{cacheKey}-{Guid.NewGuid():N}.tmp.jpg");
                try
                {
                    if (category == "image" && !fullPreview && File.Exists(fullPreviewPath))
                    {
                        await GenerateDerivedThumbnailAsync(fullPreviewPath, temporaryOutput, normalizedWidth,
                            cancellationToken);
                    }
                    else
                    {
                        var downloaded = await _remoteFiles.DownloadAsync(profile, path, cancellationToken);
                        await using (downloaded.Stream)
                        {
                            if (category == "image")
                            {
                                if (originalPreview)
                                {
                                    await using var output = new FileStream(temporaryOutput, FileMode.Create,
                                        FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                                    await downloaded.Stream.CopyToAsync(output, cancellationToken);
                                }
                                else
                                {
                                    await GenerateImageAsync(downloaded.Stream, temporaryOutput,
                                        fullPreview ? null : normalizedWidth, extension,
                                        fullPreview ? _options.PreviewQuality : _options.ThumbnailQuality, cancellationToken);
                                }
                            }
                            else
                            {
                                await GenerateVideoThumbnailAsync(downloaded.Stream, temporaryOutput, normalizedWidth,
                                    cancellationToken);
                            }
                        }
                    }

                    File.Move(temporaryOutput, cachedPath, true);
                    return new ThumbnailFile(cachedPath, contentType, etag);
                }
                catch (Exception exception) when (exception is MagickException or InvalidOperationException or IOException or Win32Exception)
                {
                    _logger.LogWarning(exception, "Could not generate a thumbnail for {RemotePath}", path);
                    return null;
                }
                finally
                {
                    if (File.Exists(temporaryOutput))
                    {
                        File.Delete(temporaryOutput);
                    }
                }
            }
            finally
            {
                _coordinator.Slots.Release();
            }
        }
        finally
        {
            itemSlot.Release();
        }
    }

    private async Task GenerateDerivedThumbnailAsync(string masterPath, string outputPath, int width,
        CancellationToken cancellationToken)
    {
        using var image = new MagickImage(masterPath);
        image.Resize(new MagickGeometry((uint)width, (uint)width)
        {
            IgnoreAspectRatio = false,
            Greater = true
        });
        image.Quality = (uint)Math.Clamp(_options.ThumbnailQuality, 50, 92);
        await image.WriteAsync(outputPath, cancellationToken);
    }

    private static async Task GenerateImageAsync(Stream source, string outputPath, int? width, string extension,
        int quality, CancellationToken cancellationToken)
    {
        var format = ResolveMagickFormat(extension);
        using var image = format.HasValue ? new MagickImage(source, format.Value) : new MagickImage(source);
        image.AutoOrient();
        if (width.HasValue)
        {
            image.Resize(new MagickGeometry((uint)width.Value, (uint)width.Value)
            {
                IgnoreAspectRatio = false,
                Greater = true
            });
        }
        image.Strip();
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)Math.Clamp(quality, 50, 100);
        await image.WriteAsync(outputPath, cancellationToken);
    }

    private static MagickFormat? ResolveMagickFormat(string extension)
    {
        var formatName = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "Jpeg",
            ".tif" or ".tiff" => "Tiff",
            ".svg" => "Svg",
            _ => extension.TrimStart('.')
        };
        return Enum.TryParse<MagickFormat>(formatName, true, out var format) ? format : null;
    }

    private static string GetBrowserImageContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".avif" => "image/avif",
            _ => "application/octet-stream"
        };
    }

    private async Task GenerateVideoThumbnailAsync(Stream source, string outputPath, int width,
        CancellationToken cancellationToken)
    {
        if (_ffmpegPath is null || source is not FileStream inputFile)
        {
            throw new InvalidOperationException("FFmpeg is not configured for video thumbnails.");
        }

        var firstAttemptError = await RunFfmpegAsync(inputFile.Name, outputPath, width, "00:00:01", cancellationToken);
        if (firstAttemptError is null)
        {
            return;
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        var fallbackError = await RunFfmpegAsync(inputFile.Name, outputPath, width, null, cancellationToken);
        if (fallbackError is not null)
        {
            throw new InvalidOperationException($"FFmpeg could not generate the video poster. {fallbackError}".Trim());
        }
    }

    private async Task<string?> RunFfmpegAsync(string inputPath, string outputPath, int width, string? seek,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath!,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        if (seek is not null)
        {
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(seek);
        }
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-an");
        startInfo.ArgumentList.Add("-sn");
        startInfo.ArgumentList.Add("-dn");
        startInfo.ArgumentList.Add("-vf");
        startInfo.ArgumentList.Add($"scale={width}:-2:force_original_aspect_ratio=decrease");
        startInfo.ArgumentList.Add("-q:v");
        startInfo.ArgumentList.Add("4");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            return string.IsNullOrWhiteSpace(error) ? $"FFmpeg exited with code {process.ExitCode}." : error.Trim();
        }

        return null;
    }

    private static string? ResolveExecutable(string configuredPath, string contentRootPath)
    {
        var environmentPath = Environment.GetEnvironmentVariable("EAGLEFILES_FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            var resolvedEnvironmentPath = Environment.ExpandEnvironmentVariables(environmentPath.Trim());
            if (File.Exists(resolvedEnvironmentPath))
            {
                return resolvedEnvironmentPath;
            }
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
        var candidate = Path.IsPathRooted(expandedPath)
            ? expandedPath
            : Path.GetFullPath(Path.Combine(contentRootPath, expandedPath));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var executableName = OperatingSystem.IsWindows() && !expandedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? $"{expandedPath}.exe"
            : expandedPath;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var pathCandidate = Path.Combine(directory.Trim(), executableName);
            if (File.Exists(pathCandidate))
            {
                return pathCandidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var commonCandidates = new[]
            {
                Path.Combine(systemDrive, "Tools", "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe")
            };
            var commonPath = commonCandidates.FirstOrDefault(File.Exists);
            if (commonPath is not null)
            {
                return commonPath;
            }
        }

        return null;
    }
}
