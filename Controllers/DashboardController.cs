using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace YourNamespace.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetDashboard()
        {
            var email = User.Identity?.Name ?? "Unknown";
            return Ok(new { success = true, message = $"Welcome to your dashboard, {email}!" });
        }
    }
}
