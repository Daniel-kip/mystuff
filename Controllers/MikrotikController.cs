using Microsoft.AspNetCore.Mvc;
using System.Text;
namespace DelTechApi.Controllers;
[ApiController]
[Route("api/[controller]")]
public class MikrotikController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        // Return sample routers list
        var data = new[] {
            new { name = "Mikro-1", ip = "192.168.88.1" },
            new { name = "Mikro-2", ip = "10.0.0.1" }
        };
        return Ok(data);
    }

    [HttpGet("/api/download/mikrotik")]
    public IActionResult Download()
    {
        // Return the .rsc file content as download
        var script = System.IO.File.Exists("mikrotik_setup.rsc") ? System.IO.File.ReadAllText("mikrotik_setup.rsc") : "/ip hotspot\nadd name=hotspot1";
        var bytes = Encoding.UTF8.GetBytes(script);
        return File(bytes, "application/octet-stream", "mikrotik_setup.rsc");
    }
}
