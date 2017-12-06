using Microsoft.AspNetCore.Mvc;

namespace catalog_outcomes.Controllers
{
    public class ResourceController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}