using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DelTechApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DelTechApi.Services
{
    public interface ISmsService
    {
        Task<BulkSmsResponse> SendBulkSmsAsync(BulkMessageRequest request);
    }

    public class AfricasTalkingSmsService : ISmsService
    {
        private readonly HttpClient _httpClient;
        private readonly AfricasTalkingSettings _settings;
        private readonly ILogger<AfricasTalkingSmsService> _logger;

        public AfricasTalkingSmsService(
            HttpClient httpClient, 
            IOptions<AfricasTalkingSettings> settings, 
            ILogger<AfricasTalkingSmsService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<BulkSmsResponse> SendBulkSmsAsync(BulkMessageRequest request)
        {
            var responseList = new List<SmsResultDetail>();
            int successCount = 0;
            decimal totalCost = 0;

            // Use username exactly as provided (some apps have spaces)
            var username = _settings.Username?.Trim();
            
            // Determine if using Sandbox
            var isSandbox = username?.Equals("sandbox", StringComparison.OrdinalIgnoreCase) ?? false;
            
            _logger.LogInformation("Sending SMS via Africa's Talking. Username: '{Username}', Environment: {Env}", 
                username, isSandbox ? "SANDBOX" : "LIVE");
            
            // Note: HttpClient BaseAddress is set in Program.cs based on configuration, 
            // but we can double check or rely on the named client configuration.

            // AfricasTalking API requires x-www-form-urlencoded
            // We process numbers in batches or individually depending on requirement.
            // For true "Bulk" via API, we can send comma-separated numbers in "to" field.
            // The API supports sending to multiple recipients in one request.

            var recipients = string.Join(",", request.PhoneNumbers);
            
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("to", recipients),
                new KeyValuePair<string, string>("message", request.Message),
                new KeyValuePair<string, string>("from", _settings.SenderName ?? "")
            });

            // Set API Key header
            if(!_httpClient.DefaultRequestHeaders.Contains("apikey"))
            {
                var apiKey = _settings.ApiKey?.Trim();
                _httpClient.DefaultRequestHeaders.Add("apikey", apiKey);
                
                // Debug log to confirm key update (first 4 chars)
                var maskedKey = apiKey != null && apiKey.Length > 4 
                    ? apiKey.Substring(0, 4) + "..." 
                    : "null/short";
                _logger.LogInformation("Using API Key: {MaskedKey}", maskedKey);
            }

            try 
            {
                var response = await _httpClient.PostAsync("version1/messaging", content);
                var rawResponse = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    try 
                    {
                        var doc = JsonDocument.Parse(rawResponse);
                        var messageData = doc.RootElement.GetProperty("SMSMessageData");
                        var recipientsArray = messageData.GetProperty("Recipients");

                        foreach (var recipient in recipientsArray.EnumerateArray())
                        {
                            var status = recipient.GetProperty("status").GetString();
                            var number = recipient.GetProperty("number").GetString();
                            var costStr = recipient.GetProperty("cost").GetString();
                            var msgId = recipient.GetProperty("messageId").GetString();

                            // Parse cost (format is usually "KES 1.00")
                            decimal cost = 0;
                            if (!string.IsNullOrEmpty(costStr))
                            {
                                var parts = costStr.Split(' ');
                                if (parts.Length > 1 && decimal.TryParse(parts[1], out var c))
                                {
                                    cost = c;
                                }
                            }

                            bool isSuccess = status == "Success" || status == "Sent" || status == "Queued";
                            
                            if (isSuccess) successCount++;
                            totalCost += cost;

                            responseList.Add(new SmsResultDetail
                            {
                                PhoneNumber = number,
                                Success = isSuccess,
                                MessageId = msgId,
                                Cost = cost,
                                Response = status // "Success", "Failed", etc.
                            });
                        }
                    }
                    catch (Exception parseEx)
                    {
                        _logger.LogError(parseEx, "Error parsing AT API response: {Response}", rawResponse);
                        // Fallback if parsing fails but request was 200 OK
                         responseList.Add(new SmsResultDetail 
                         { 
                             PhoneNumber = "Batch", 
                             Success = true, 
                             Response = "Sent but failed to parse details: " + rawResponse 
                         });
                         successCount = request.PhoneNumbers.Count;
                    }
                }
                else
                {
                    _logger.LogWarning("AT API Error: {StatusCode} - {Response}", response.StatusCode, rawResponse);
                    // Add failure entry for all numbers if batch fails
                    foreach(var num in request.PhoneNumbers)
                    {
                        responseList.Add(new SmsResultDetail
                        {
                            PhoneNumber = num,
                            Success = false,
                            Response = rawResponse // Capture the error message
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HTTP Request Exception");
                 foreach(var num in request.PhoneNumbers)
                {
                    responseList.Add(new SmsResultDetail
                    {
                        PhoneNumber = num,
                        Success = false,
                        Response = ex.Message
                    });
                }
            }

            return new BulkSmsResponse
            {
                Success = successCount > 0,
                Total = request.PhoneNumbers.Count,
                Successful = successCount,
                Results = responseList
            };
        }
    }

    // Response Models matching Frontend expectations
    public class BulkSmsResponse
    {
        public bool Success { get; set; }
        public int Total { get; set; }
        public int Successful { get; set; }
        public List<SmsResultDetail> Results { get; set; } = new List<SmsResultDetail>();
    }

    public class SmsResultDetail
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string MessageId { get; set; }
        public decimal Cost { get; set; }
        public string Response { get; set; } = string.Empty;
    }
}
