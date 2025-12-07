using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DelTechApi.Models;
using DelTechApi.Services;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace DelTechApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Allow any authenticated user
    public class MessagingController : ControllerBase
    {
        private readonly IMessageLogService _logService;
        private readonly ISmsService _smsService;
        private readonly ILogger<MessagingController> _logger;
        private readonly ICacheService _cacheService;
        private const int RateLimitCount = 10;
        private readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

        public MessagingController(
            IMessageLogService logService, 
            ISmsService smsService,
            ILogger<MessagingController> logger,
            ICacheService cacheService)
        {
            _logService = logService;
            _smsService = smsService;
            _logger = logger;
            _cacheService = cacheService;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] BulkMessageRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var cacheKey = $"sms_rate_limit_{userId}";

                // 1. Rate limiting check
                var rateLimit = await _cacheService.GetAsync<RateLimitInfo>(cacheKey);
                if (rateLimit?.Count >= RateLimitCount)
                {
                    _logger.LogWarning("Rate limit exceeded for user {UserId}", userId);
                    return StatusCode(429, new { 
                        success = false, 
                        message = "Rate limit exceeded. Please try again later." 
                    });
                }

                // 2. Validate request (Basic validation, detailed validation can be in service or fluent validation)
                if (request.PhoneNumbers == null || request.PhoneNumbers.Count == 0)
                    return BadRequest(new { success = false, message = "At least one phone number is required." });

                // 3. Update rate limit
                await UpdateRateLimit(cacheKey, rateLimit);

                // 4. Send via Service
                _logger.LogInformation("User {UserId} sending messages to {Count} recipients", userId, request.PhoneNumbers.Count);
                
                var result = await _smsService.SendBulkSmsAsync(request);

                // 5. Log results to Database
                var now = DateTime.UtcNow;
                foreach(var item in result.Results)
                {
                    var log = new MessageLog
                    {
                        UserId = userId,
                        Recipient = item.PhoneNumber,
                        MessageText = request.Message,
                        SentAt = now,
                        Status = item.Success ? "Delivered" : "Failed",
                        Response = item.Response,
                        MessageId = item.MessageId,
                        Cost = item.Cost
                    };
                    await _logService.AddLogAsync(log);
                }

                _logger.LogInformation("User {UserId} sent messages. Success: {Success}/{Total}", userId, result.Successful, result.Total);

                return Ok(new { 
                    success = result.Success, 
                    total = result.Total,
                    successful = result.Successful,
                    failed = result.Total - result.Successful,
                    results = result.Results,
                    totalCost = result.Results.Sum(r => r.Cost) // Added convenience field
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending messages for user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { 
                    success = false, 
                    message = "An error occurred while sending messages." 
                });
            }
        }

        // Removed SendSingleSms endpoint as SendMessage handles both single and bulk via list.
        // If single endpoint is needed for legacy compatibility, it can just wrap the bulk service call.


        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs([FromQuery] MessageLogQuery query)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                
                if (userRole != "Admin")
                {
                    query.UserId = userId;
                }

                var logs = await _logService.GetLogsAsync(query);
                var totalCount = await _logService.GetLogsCountAsync(query);

                Response.Headers["X-Total-Count"] = totalCount.ToString();
                
                return Ok(new { 
                    success = true, 
                    logs,
                    total = totalCount,
                    page = query.Page,
                    pageSize = query.PageSize
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving message logs for user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { 
                    success = false, 
                    message = "An error occurred while retrieving logs." 
                });
            }
        }

        [HttpGet("balance")]
        public Task<IActionResult> GetBalance()
        {
             // To implement: Add GetBalanceAsync to ISmsService
             return Task.FromResult<IActionResult>(Ok(new { success = true, balance = "N/A", currency = "KES" }));
        }

        private async Task UpdateRateLimit(string cacheKey, RateLimitInfo? existingLimit)
        {
            var newLimit = existingLimit ?? new RateLimitInfo { Count = 0, FirstRequest = DateTime.UtcNow };
            newLimit.Count++;
            
            await _cacheService.SetAsync(cacheKey, newLimit, RateLimitWindow);
        }
    }
}