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
    [Authorize(Policy = "SmsAccess")]
    public class MessagingController : ControllerBase
    {
        private readonly IMessageLogService _logService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MessagingController> _logger;
        private readonly ICacheService _cacheService;
        private readonly AfricasTalkingSettings _atSettings;
        private const int RateLimitCount = 10;
        private readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

        public MessagingController(
            IMessageLogService logService, 
            IHttpClientFactory httpClientFactory,
            ILogger<MessagingController> logger,
            ICacheService cacheService,
            AfricasTalkingSettings atSettings)
        {
            _logService = logService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _cacheService = cacheService;
            _atSettings = atSettings;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] BulkMessageRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var cacheKey = $"sms_rate_limit_{userId}";

                // Rate limiting check
                var rateLimit = await _cacheService.GetAsync<RateLimitInfo>(cacheKey);
                if (rateLimit?.Count >= RateLimitCount)
                {
                    _logger.LogWarning("Rate limit exceeded for user {UserId}", userId);
                    return StatusCode(429, new { 
                        success = false, 
                        message = "Rate limit exceeded. Please try again later." 
                    });
                }

                // Validate request
                var validationResult = ValidateRequest(request);
                if (!validationResult.IsValid)
                    return BadRequest(new { success = false, message = validationResult.ErrorMessage });

                // Update rate limit
                await UpdateRateLimit(cacheKey, rateLimit);

                var client = _httpClientFactory.CreateClient("AfricasTalking");
                var results = new List<MessageSendResult>();
                var now = DateTime.UtcNow;

                _logger.LogInformation("User {UserId} sending {Count} messages", userId, request.PhoneNumbers.Count);

                // Process messages in batches
                var batches = request.PhoneNumbers.Chunk(50);
                
                foreach (var batch in batches)
                {
                    var batchResults = await ProcessBatch(client, batch, request.Message, now, userId);
                    results.AddRange(batchResults);
                    
                    await Task.Delay(100);
                }

                _logger.LogInformation("User {UserId} successfully sent {SuccessCount}/{TotalCount} messages", 
                    userId, results.Count(r => r.Success), results.Count);

                return Ok(new { 
                    success = true, 
                    total = request.PhoneNumbers.Count,
                    successful = results.Count(r => r.Success),
                    failed = results.Count(r => !r.Success),
                    results 
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

        [HttpPost("send-single")]
        public async Task<IActionResult> SendSingleMessage([FromBody] SingleMessageRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                
                if (string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Message))
                    return BadRequest(new { success = false, message = "Phone number and message are required." });

                if (!IsValidPhoneNumber(request.PhoneNumber))
                    return BadRequest(new { success = false, message = "Invalid phone number format." });

                var client = _httpClientFactory.CreateClient("AfricasTalking");
                var now = DateTime.UtcNow;

                var result = await SendSingleSms(client, request.PhoneNumber, request.Message, now, userId);

                if (result.Success)
                {
                    _logger.LogInformation("User {UserId} successfully sent message to {PhoneNumber}", 
                        userId, request.PhoneNumber);
                }
                else
                {
                    _logger.LogWarning("User {UserId} failed to send message to {PhoneNumber}: {Error}", 
                        userId, request.PhoneNumber, result.Response);
                }

                return Ok(new { 
                    success = result.Success, 
                    result.PhoneNumber,
                    result.Response,
                    messageId = result.MessageId 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending single message for user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { 
                    success = false, 
                    message = "An error occurred while sending the message." 
                });
            }
        }

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
        public async Task<IActionResult> GetBalance()
        {
            try
            {
                var client = _httpClientFactory.CreateClient("AfricasTalking");
                
                var url = $"https://api.africastalking.com/version1/user?username={_atSettings.Username}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("apikey", _atSettings.ApiKey);

                var response = await client.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch balance: {Response}", content);
                    return StatusCode(500, new { 
                        success = false, 
                        message = "Failed to retrieve balance information." 
                    });
                }

                using var doc = JsonDocument.Parse(content);
                var balance = doc.RootElement.GetProperty("UserData").GetProperty("balance").GetString();

                _logger.LogInformation("Balance checked by user {UserId}: {Balance}", 
                    User.FindFirst(ClaimTypes.NameIdentifier)?.Value, balance);

                return Ok(new { 
                    success = true, 
                    balance,
                    currency = "KES"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking balance for user {UserId}", User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
                return StatusCode(500, new { 
                    success = false, 
                    message = "An error occurred while checking balance." 
                });
            }
        }

        // ===================== Helper Methods =====================

        private async Task<List<MessageSendResult>> ProcessBatch(
            HttpClient client, 
            string[] phoneNumbers, 
            string message, 
            DateTime timestamp,
            int userId)
        {
            var results = new List<MessageSendResult>();

            foreach (var number in phoneNumbers)
            {
                if (string.IsNullOrWhiteSpace(number))
                    continue;

                var result = await SendSingleSms(client, number, message, timestamp, userId);
                results.Add(result);
            }

            return results;
        }

        private async Task<MessageSendResult> SendSingleSms(
            HttpClient client, 
            string phoneNumber, 
            string message, 
            DateTime timestamp,
            int userId)
        {
            var formattedNumber = FormatPhoneNumber(phoneNumber);
            
            var payload = new
            {
                username = _atSettings.Username,
                to = formattedNumber,
                message = message,
                from = _atSettings.SenderName
            };

            var json = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.africastalking.com/version1/messaging")
                {
                    Content = json
                };
                request.Headers.Add("apikey", _atSettings.ApiKey);

                var response = await client.SendAsync(request);
                var resultContent = await response.Content.ReadAsStringAsync();
                var success = response.IsSuccessStatusCode;

                string? messageId = null;
                if (success)
                {
                    using var doc = JsonDocument.Parse(resultContent);
                    var recipients = doc.RootElement.GetProperty("SMSMessageData").GetProperty("Recipients");
                    if (recipients.GetArrayLength() > 0)
                    {
                        messageId = recipients[0].GetProperty("messageId").GetString();
                    }
                }

                // Set status based on success instead of trying to set the read-only Success property
                var status = success ? "Delivered" : "Failed";
                
                var log = new MessageLog
                {
                    UserId = userId,
                    Recipient = formattedNumber,
                    MessageText = message,
                    SentAt = timestamp,
                    Status = status,
                    Response = resultContent,
                    MessageId = messageId,
                    Cost = success ? CalculateCost(message) : 0
                };

                await _logService.AddLogAsync(log);

                return new MessageSendResult
                {
                    PhoneNumber = formattedNumber,
                    Success = success,
                    Response = resultContent,
                    MessageId = messageId,
                    Cost = log.Cost
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending SMS to {PhoneNumber}", formattedNumber);
                
                var log = new MessageLog
                {
                    UserId = userId,
                    Recipient = formattedNumber,
                    MessageText = message,
                    SentAt = timestamp,
                    Status = "Failed",
                    Response = ex.Message,
                    Cost = 0
                };

                await _logService.AddLogAsync(log);

                return new MessageSendResult
                {
                    PhoneNumber = formattedNumber,
                    Success = false,
                    Response = ex.Message,
                    Cost = 0
                };
            }
        }

        private (bool IsValid, string ErrorMessage) ValidateRequest(BulkMessageRequest request)
        {
            if (request.PhoneNumbers == null || request.PhoneNumbers.Count == 0)
                return (false, "At least one phone number is required.");

            if (string.IsNullOrWhiteSpace(request.Message))
                return (false, "Message text is required.");

            if (request.Message.Length > 160)
                return (false, "Message must be 160 characters or less.");

            if (request.PhoneNumbers.Count > 1000)
                return (false, "Maximum of 1000 phone numbers allowed per request.");

            foreach (var number in request.PhoneNumbers)
            {
                if (!IsValidPhoneNumber(number))
                    return (false, $"Invalid phone number format: {number}");
            }

            return (true, string.Empty);
        }

        private bool IsValidPhoneNumber(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return false;

            var cleaned = new string(number.Where(char.IsDigit).ToArray());
            return cleaned.Length >= 10 && cleaned.Length <= 15;
        }

        private string FormatPhoneNumber(string number)
        {
            var cleaned = new string(number.Where(char.IsDigit).ToArray());
            if (cleaned.StartsWith("0") && cleaned.Length == 10)
            {
                return "+254" + cleaned.Substring(1);
            }
            else if (!cleaned.StartsWith("+"))
            {
                return "+" + cleaned;
            }
            return number;
        }

        private decimal CalculateCost(string message)
        {
            int segments = (int)Math.Ceiling(message.Length / 160.0);
            return segments * 1.0m;
        }

        private async Task UpdateRateLimit(string cacheKey, RateLimitInfo? existingLimit)
        {
            var newLimit = existingLimit ?? new RateLimitInfo { Count = 0, FirstRequest = DateTime.UtcNow };
            newLimit.Count++;
            
            await _cacheService.SetAsync(cacheKey, newLimit, RateLimitWindow);
        }
    }
}