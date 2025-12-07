using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController : ControllerBase
{
    private readonly UserSettingsService _settingsService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(UserSettingsService settingsService, ILogger<SettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                          ?? User.FindFirst("id");

        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
            return Unauthorized("Invalid or missing user ID in token.");

        var settings = await _settingsService.GetSettingsAsync(userId);

        if (settings == null)
            return NotFound("User settings not found.");

        return Ok(settings);
    }

    [HttpPost]
    public async Task<IActionResult> SaveSettings([FromBody] UserSettings settings)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier) 
                              ?? User.FindFirst("id");

            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized("Invalid or missing user ID in token.");

            await _settingsService.SaveSettingsAsync(userId, settings);

            return Ok(new { success = true, message = "Settings saved successfully" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n\n!!!!!! SAVE SETTINGS ERROR: {ex.Message} !!!!!!\n\n");
            _logger.LogError(ex, "CRITICAL ERROR: SaveSettings failed.");
            return StatusCode(500, new { success = false, message = "Save failed: " + ex.Message, details = ex.ToString() });
        }
    }
}
