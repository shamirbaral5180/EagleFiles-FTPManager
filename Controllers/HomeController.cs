using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FTP_WebApp.Models;
using Microsoft.AspNetCore.Diagnostics;

namespace FTP_WebApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Connections");
        }

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var failure = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (failure is not null)
        {
            _logger.LogError(failure.Error, "Request failed on {Path}. Request ID: {RequestId}", failure.Path,
                Activity.Current?.Id ?? HttpContext.TraceIdentifier);
        }
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
