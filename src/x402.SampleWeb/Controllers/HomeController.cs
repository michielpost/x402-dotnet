using Microsoft.AspNetCore.Mvc;

namespace x402.SampleWeb.Controllers
{
    [Route("/")]
    public class HomeController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return Content("Test");
        }
    }
}
