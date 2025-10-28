using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PicklePlay.Web.Models;

namespace PicklePlay.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }
    // In HomeController.cs
    [HttpGet]
    public IActionResult Community()
    {
        // Check if user is logged in
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            TempData["ErrorMessage"] = "Please login first.";
            return RedirectToAction("Login", "Auth");
        }

        return View();
    }

    [HttpGet]
    public IActionResult CommunityPages(int? id)
    {
        // Check if user is logged in
        var userEmail = HttpContext.Session.GetString("UserEmail");
        if (string.IsNullOrEmpty(userEmail))
        {
            TempData["ErrorMessage"] = "Please login first.";
            return RedirectToAction("Login", "Auth");
        }

        // You can use the id parameter to load specific community data
        ViewBag.CommunityId = id;
        return View();
    }
    public IActionResult Index()
    
    {
        return RedirectToAction("Login", "Auth");  // ADD this line
    }
    

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
