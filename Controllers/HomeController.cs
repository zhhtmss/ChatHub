using Microsoft.AspNetCore.Mvc;

namespace WebApplication24.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
