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
    private readonly IRemoteFileService _remoteFiles;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly MediaOptions _options;
    private readonly string _cachePath;
    private readonly string? _ffmpegPath;
    private readonly ThumbnailGenerationCoordinator _coordinator;

    public ThumbnailService(IRemoteFileService remoteFiles, IWebHostEnvironment environment,
        IOptions<MediaOptions> options, ILogger<ThumbnailService> logger, ThumbnailGenerationCoordinator coordinator)
    {
        _remoteFiles = remoteFiles;
        _logger = logger;
        _options = options.Value;
        _coordinator = coordinator;
        _cachePath = Path.IsPathRooted(_options.ThumbnailCachePath)
            ? _options.ThumbnailCachePath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.ThumbnailCachePath));
        _ffmpegPath = ResolveExecutable(_options.FfmpegPath, environment.ContentRootPath);
        Directory.CreateDirectory(_cachePath);
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

        var normalizedWidth = AllowedWidths.OrderBy(candidate => Math.Abs(candidate - width)).First();
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{profile.Id}|{path}|{version}"))).ToLowerInvariant();
        var masterPath = Path.Combine(_cachePath, $"{cacheKey}-master.jpg");
        var cachedPath = normalizedWidth == 1600
            ? masterPath
            : Path.Combine(_cachePath, $"{cacheKey}-{normalizedWidth}.jpg");
        var etag = $"\"{cacheKey}-{normalizedWidth}\"";
        if (File.Exists(cachedPath))
        {
            return new ThumbnailFile(cachedPath, "image/jpeg", etag);
        }

        var itemSlot = _coordinator.GetItemSlot(cacheKey);
        await itemSlot.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cachedPath))
            {
                return new ThumbnailFile(cachedPath, "image/jpeg", etag);
            }

            await _coordinator.Slots.WaitAsync(cancellationToken);
            try
            {
                var temporaryMaster = Path.Combine(_cachePath, $".{cacheKey}-{Guid.NewGuid():N}.master.tmp.jpg");
                var temporaryOutput = Path.Combine(_cachePath, $".{cacheKey}-{Guid.NewGuid():N}.tmp.jpg");
                try
                {
                    if (!File.Exists(masterPath))
                    {
                        var downloaded = await _remoteFiles.DownloadAsync(profile, path, cancellationToken);
                        await using (downloaded.Stream)
                        {
                            if (category == "image")
                            {
                                await GenerateImageThumbnailAsync(downloaded.Stream, temporaryMaster, 1600, extension,
                                    cancellationToken);
                            }
                            else
                            {
                                await GenerateVideoThumbnailAsync(downloaded.Stream, temporaryMaster, 1600, cancellationToken);
                            }
                        }

                        File.Move(temporaryMaster, masterPath, true);
                    }

                    if (normalizedWidth == 1600)
                    {
                        return new ThumbnailFile(masterPath, "image/jpeg", etag);
                    }

                    await GenerateDerivedThumbnailAsync(masterPath, temporaryOutput, normalizedWidth, cancellationToken);
                    File.Move(temporaryOutput, cachedPath, true);
                    return new ThumbnailFile(cachedPath, "image/jpeg", etag);
                }
                catch (Exception exception) when (exception is MagickException or InvalidOperationException or IOException)
                {
                    _logger.LogWarning(exception, "Could not generate a thumbnail for {RemotePath}", path);
                    return null;
                }
                finally
                {
                    if (File.Exists(temporaryMaster))
                    {
                        File.Delete(temporaryMaster);
                    }
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

    private async Task GenerateImageThumbnailAsync(Stream source, string outputPath, int width, string extension,
        CancellationToken cancellationToken)
    {
        var format = ResolveMagickFormat(extension);
        using var image = format.HasValue ? new MagickImage(source, format.Value) : new MagickImage(source);
        image.AutoOrient();
        var geometry = new MagickGeometry((uint)width, (uint)width)
        {
            IgnoreAspectRatio = false,
            Greater = true
        };
        image.Resize(geometry);
        image.Strip();
        image.BackgroundColor = MagickColors.White;
        image.Alpha(AlphaAction.Remove);
        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)Math.Clamp(_options.ThumbnailQuality, 50, 92);
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

    private async Task GenerateVideoThumbnailAsync(Stream source, string outputPath, int width,
        CancellationToken cancellationToken)
    {
        if (_ffmpegPath is null || source is not FileStream inputFile)
        {
            throw new InvalidOperationException("FFmpeg is not configured for video thumbnails.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add("00:00:01");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputFile.Name);
        startInfo.ArgumentList.Add("-frames:v");
        startInfo.ArgumentList.Add("1");
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
            throw new InvalidOperationException($"FFmpeg could not generate the video poster. {error}".Trim());
        }
    }

    private static string? ResolveExecutable(string configuredPath, string contentRootPath)
    {
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

        return null;
    }
}
