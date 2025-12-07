using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DelTechApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DelTechApi.Services
{
    public class InfobipSmsService : ISmsService
    {
        private readonly HttpClient _httpClient;
        private readonly InfobipSettings _settings;
        private readonly ILogger<InfobipSmsService> _logger;

        public InfobipSmsService(
            HttpClient httpClient, 
            IOptions<InfobipSettings> settings, 
            ILogger<InfobipSmsService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<BulkSmsResponse> SendBulkSmsAsync(BulkMessageRequest request)
        {
            var responseList = new List<SmsResultDetail>();

            // 1. Construct Infobip Payload
            // Docs: https://www.infobip.com/docs/api/channels/sms/sms-messaging/outbound-sms/send-sms-message
            var payload = new
            {
                messages = new[]
                {
                    new
                    {
                        from = _settings.SenderId,
                        destinations = request.PhoneNumbers.Select(num => new { to = num }).ToArray(),
                        text = request.Message
                    }
                }
            };

            // 2. Prepare Request
            // HttpClient BaseAddress and Auth header are configured in Program.cs
            // But strict requirement: Authorization: App {ApiKey}
            // We ensure it here just case or rely on Program.cs injection.
            // Best practice: Program.cs sets BaseUrl, but we can set Auth header here dynamically if preferred/or in Named Client.
            
            // Let's assume Program.cs sets BaseUrl. We verify/set Auth Header.
            if (!_httpClient.DefaultRequestHeaders.Contains("Authorization"))
            {
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"App {_settings.ApiKey}");
            }
            
            try
            {
                _logger.LogInformation("Sending SMS via Infobip from {SenderId} to {Count} recipients", _settings.SenderId, request.PhoneNumbers.Count);

                var response = await _httpClient.PostAsJsonAsync("sms/2/text/advanced", payload);
                var rawResponse = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Parse Success Response
                    var infobipResponse = JsonSerializer.Deserialize<InfobipResponse>(rawResponse, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (infobipResponse?.Messages != null)
                    {
                        foreach (var msg in infobipResponse.Messages)
                        {
                            var isSuccess = msg.Status.GroupName == "PENDING" || msg.Status.GroupName == "ACCEPTED" || msg.Status.GroupName == "DELIVERED";
                            
                            responseList.Add(new SmsResultDetail
                            {
                                PhoneNumber = msg.To,
                                Success = isSuccess,
                                MessageId = msg.MessageId,
                                Cost = 0, // Infobip doesn't enforce cost per request in basic response, usually separate lookup
                                Response = $"{msg.Status.GroupName}: {msg.Status.Name}"
                            });
                        }
                    }
                    else
                    {
                         // Fallback if parsing structure fails
                         foreach(var num in request.PhoneNumbers)
                            responseList.Add(new SmsResultDetail { PhoneNumber = num, Success = true, Response = "Sent (Details parsing failed)" });
                    }
                }
                else
                {
                    _logger.LogWarning("Infobip API Error: {StatusCode} - {Response}", response.StatusCode, rawResponse);
                     foreach(var num in request.PhoneNumbers)
                         responseList.Add(new SmsResultDetail { PhoneNumber = num, Success = false, Response = "Provider Error: " + rawResponse });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Infobip HTTP Request Exception");
                foreach (var num in request.PhoneNumbers)
                    responseList.Add(new SmsResultDetail { PhoneNumber = num, Success = false, Response = ex.Message });
            }

            return new BulkSmsResponse
            {
                Success = responseList.Any(r => r.Success),
                Total = request.PhoneNumbers.Count,
                Successful = responseList.Count(r => r.Success),
                Results = responseList
            };
        }
    }

    // Infobip Response Models (Private helpers for deserialization)
    public class InfobipResponse
    {
        public List<InfobipMessage> Messages { get; set; }
    }

    public class InfobipMessage
    {
        public string MessageId { get; set; }
        public InfobipStatus Status { get; set; }
        public string To { get; set; }
    }

    public class InfobipStatus
    {
        public int Id { get; set; }
        public string GroupId { get; set; }
        public string GroupName { get; set; }
        public string Name { get; set; }
    }
}
