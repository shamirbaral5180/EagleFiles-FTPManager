using System.Security.Claims;
using FTP_WebApp.Models;
using FTP_WebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FTP_WebApp.Controllers;

[Authorize]
public class ConnectionsController : Controller
{
    private readonly IConnectionProfileService _profiles;

    public ConnectionsController(IConnectionProfileService profiles)
    {
        _profiles = profiles;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var connections = await _profiles.GetAllAsync(UserId, cancellationToken);
        return View(new ConnectionsIndexViewModel { Connections = connections });
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View("Form", new ConnectionProfileInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ConnectionProfileInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Form", input);
        }

        try
        {
            var id = await _profiles.SaveAsync(input, UserId, cancellationToken);
            TempData["Success"] = "Connection saved. Open it to verify the server settings.";
            return RedirectToAction("Index", "Files", new { id });
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("Form", input);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(id, UserId, cancellationToken);
        return View("Form", new ConnectionProfileInput
        {
            Id = profile.Id,
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            SecurityMode = profile.SecurityMode,
            RootPath = profile.RootPath
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ConnectionProfileInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Form", input);
        }

        try
        {
            await _profiles.SaveAsync(input, UserId, cancellationToken);
            TempData["Success"] = "Connection updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("Form", input);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        await _profiles.DeleteAsync(id, UserId, cancellationToken);
        TempData["Success"] = "Connection removed.";
        return RedirectToAction(nameof(Index));
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
                             ?? throw new UnauthorizedAccessException("A signed-in user is required.");
}
