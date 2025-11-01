using Microsoft.AspNetCore.Mvc;

public class AdminController : Controller
{
    public IActionResult AdminDashboard()
    {
        // Manual session check instead of [Authorize]
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult SuspendList()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult TransactionLog()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult CommunityRequests()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult InactiveCommunities()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult EscrowDashboard()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult EscrowTransaction()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }

    public IActionResult Refund()
    {
        var userRole = HttpContext.Session.GetString("UserRole");
        if (userRole != "Admin")
        {
            return RedirectToAction("Login", "Auth");
        }
        return View();
    }
}