using System.Diagnostics;
using gc2lti.Models;
using Microsoft.AspNetCore.Mvc;

namespace gc2lti_outcomes.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
