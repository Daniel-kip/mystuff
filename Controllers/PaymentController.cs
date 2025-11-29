using Microsoft.AspNetCore.Mvc;

namespace DelTechApi.Controllers;
[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    [HttpPost("stkpush")]
    public IActionResult StkPush([FromBody] object payload)
    {
        // Placeholder for MPESA STK Push integration.
        return Ok(new { status = "mocked", payload });
    }

    [HttpPost("callback")]
    public IActionResult Callback([FromBody] object callback)
    {
        // Handle MPESA callbacks here (verify and store)
        return Ok();
    }
}
