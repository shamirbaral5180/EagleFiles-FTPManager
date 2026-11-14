using FTP_WebApp.Models;
using Microsoft.Extensions.Options;

namespace FTP_WebApp.Services;

public class StoragePathProvider : IStoragePathProvider
{
    private readonly ILogger<StoragePathProvider> _logger;

    public StoragePathProvider(IWebHostEnvironment environment, IOptions<StorageOptions> options,
        ILogger<StoragePathProvider> logger)
    {
        _logger = logger;
        var configuredPath = options.Value.DataPath;
        var preferredPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));

        if (!TryEnsureDirectory(preferredPath))
        {
            throw new InvalidOperationException($"EagleFiles cannot write to '{preferredPath}'. Grant the IIS application pool identity Modify permission on this directory. Storage is not relocated automatically to protect existing accounts and encryption keys.");
        }
        RootPath = preferredPath;
        UsingFallbackPath = false;

        DataProtectionKeyPath = Path.Combine(RootPath, "DataProtectionKeys");
        LogPath = Path.Combine(RootPath, "logs");
        TryEnsureDirectory(DataProtectionKeyPath);
        TryEnsureDirectory(LogPath);
    }

    public string RootPath { get; }

    public string DataProtectionKeyPath { get; }

    public string LogPath { get; }

    public bool UsingFallbackPath { get; }

    public string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RootPath;
        }

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(RootPath, path));
    }

    private static bool TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
