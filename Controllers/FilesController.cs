using System.Security.Claims;
using FTP_WebApp.Models;
using FTP_WebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FTP_WebApp.Controllers;

[Authorize]
public class FilesController : Controller
{
    private readonly IConnectionProfileService _profiles;
    private readonly IRemoteFileService _files;
    private readonly IThumbnailService _thumbnails;

    public FilesController(IConnectionProfileService profiles, IRemoteFileService files, IThumbnailService thumbnails)
    {
        _profiles = profiles;
        _files = files;
        _thumbnails = thumbnails;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int id, string? path, string? search, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        var normalizedPath = NormalizePath(path);

        try
        {
            var items = await _files.ListAsync(profile, normalizedPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(search))
            {
                items = items.Where(item => item.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return View(CreateViewModel(profile, normalizedPath, search, items));
        }
        catch (System.Security.Authentication.AuthenticationException)
        {
            ViewData["ConnectionError"] = "The FTP TLS handshake failed.";
            ViewData["ConnectionErrorDetail"] = "For a plain FTP server on port 21, select PlainFtp. A TLS frame error can indicate the wrong security mode or port; a certificate rejection requires checking certificate trust and host name. See the application log for the specific cause. TLS is never silently downgraded.";
            return View(CreateViewModel(profile, normalizedPath, search, []));
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or System.Net.Sockets.SocketException
                                         or FluentFTP.Exceptions.FtpException)
        {
            ViewData["ConnectionError"] = "EagleFiles could not reach this server. Check the host, port, credentials, and TLS mode.";
            ViewData["ConnectionErrorDetail"] = exception.Message;
            return View(CreateViewModel(profile, normalizedPath, search, []));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Download(int id, string path, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        var file = await _files.DownloadAsync(profile, path, cancellationToken);
        return File(file.Stream, file.ContentType, file.FileName, enableRangeProcessing: true);
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, string path, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        var file = await _files.DownloadAsync(profile, path, cancellationToken);
        Response.Headers.ContentDisposition = $"inline; filename=\"{file.FileName.Replace("\"", string.Empty)}\"";
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpGet]
    public async Task<IActionResult> Thumbnail(int id, string path, int width = 640, string? version = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        var thumbnail = await _thumbnails.GetAsync(profile, path, width, version, cancellationToken);
        if (thumbnail is null)
        {
            return NotFound();
        }
        if (Request.Headers.IfNoneMatch.Contains(thumbnail.ETag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = thumbnail.ETag;
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(thumbnail.FilePath, thumbnail.ContentType);
    }

    [HttpGet]
    public async Task<IActionResult> Open(int id, string path, string? version, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return RedirectToAction(nameof(Index), new { id });
        }

        var fileName = normalizedPath.Split('/')[^1];
        var extension = Path.GetExtension(fileName);
        var separator = normalizedPath.LastIndexOf('/');
        return View(new FilePreviewViewModel
        {
            Connection = profile,
            Path = normalizedPath,
            FileName = fileName,
            ParentPath = separator < 0 ? string.Empty : normalizedPath[..separator],
            Category = FileTypeCatalog.GetCategory(extension),
            TypeLabel = FileTypeCatalog.GetTypeLabel(extension),
            CanPreview = FileTypeCatalog.CanPreview(extension),
            Version = version ?? string.Empty
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(536_870_912)]
    public async Task<IActionResult> Upload(int id, string? path, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            TempData["Error"] = "Choose a non-empty file to upload.";
            return RedirectToFolder(id, path);
        }

        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        await using var stream = file.OpenReadStream();
        await _files.UploadAsync(profile, path, file.FileName, stream, cancellationToken);
        TempData["Success"] = $"{file.FileName} uploaded.";
        return RedirectToFolder(id, path);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(int id, string? path, string name, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        await _files.CreateFolderAsync(profile, path, name, cancellationToken);
        TempData["Success"] = $"Folder {name} created.";
        return RedirectToFolder(id, path);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, string path, string currentPath, string newName,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        await _files.RenameAsync(profile, currentPath, newName, cancellationToken);
        TempData["Success"] = "Item renamed.";
        return RedirectToFolder(id, path);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, string path, string sourcePath, string destinationFolder, bool isDirectory,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        await _files.MoveAsync(profile, sourcePath, destinationFolder, isDirectory, cancellationToken);
        TempData["Success"] = "Item moved.";
        return RedirectToFolder(id, path);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string path, string targetPath, bool isDirectory,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        await _files.DeleteAsync(profile, targetPath, isDirectory, cancellationToken);
        TempData["Success"] = "Item deleted.";
        return RedirectToFolder(id, path);
    }

    private FileBrowserViewModel CreateViewModel(FtpConnectionProfile profile, string path, string? search,
        IReadOnlyList<RemoteFileItem> items)
    {
        var breadcrumbs = new List<BreadcrumbItem> { new("Home", string.Empty) };
        var current = string.Empty;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : $"{current}/{segment}";
            breadcrumbs.Add(new BreadcrumbItem(segment, current));
        }

        var separator = path.LastIndexOf('/');
        var parent = separator < 0 ? string.Empty : path[..separator];
        return new FileBrowserViewModel
        {
            Connection = profile,
            Path = path,
            ParentPath = parent,
            Search = search?.Trim() ?? string.Empty,
            Breadcrumbs = breadcrumbs,
            Items = items
        };
    }

    private IActionResult RedirectToFolder(int id, string? path)
    {
        return RedirectToAction(nameof(Index), new { id, path = NormalizePath(path) });
    }

    private static string NormalizePath(string? path)
    {
        return string.Join('/', (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? throw new UnauthorizedAccessException("A signed-in user is required.");
}
