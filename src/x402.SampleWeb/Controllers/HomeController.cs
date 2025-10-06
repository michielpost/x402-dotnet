using Microsoft.AspNetCore.Mvc;
using x402.Attributes;

namespace x402.SampleWeb.Controllers
{
    [Route("{controller}")]
    public class HomeController : Controller
    {
        /// <summary>
        /// Protected by Middleware
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/")]
        public IActionResult Index()
        {
            return Content("Test");
        }

        [HttpGet]
        [Route("free")]
        public IActionResult Free()
        {
            return Content("Free");
        }

        [HttpGet]
        [Route("protected")]
        [PaymentRequired("1", "USDC", "0x", "base-sepolia")]
        public IActionResult Protected()
        {
            return Content("Protected");
        }
    }
}
