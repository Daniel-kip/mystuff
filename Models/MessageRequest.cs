using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DelTechApi.Models
{
    public class MessageRequest
    {
        [JsonIgnore]
        public int? SenderId { get; set; }

        [Required(ErrorMessage = "At least one phone number is required")]
        [MinLength(1, ErrorMessage = "At least one phone number is required")]
        public List<string> PhoneNumbers { get; set; } = new List<string>();

        [Required(ErrorMessage = "Message text is required")]
        [StringLength(1600, ErrorMessage = "Message cannot exceed 1600 characters")]
        public string Message { get; set; } = string.Empty;

        [StringLength(11, ErrorMessage = "Sender ID cannot exceed 11 characters")]
        public string SenderIdCustom { get; set; } = string.Empty;

        public bool IsPriority { get; set; } = false;

        public MessageType MessageType { get; set; } = MessageType.Promotional;

        [JsonIgnore]
        public string RequestId { get; set; } = string.Empty;

        [JsonIgnore]
        public string ClientIp { get; set; } = string.Empty;

        [JsonIgnore]
        public string UserAgent { get; set; } = string.Empty;
    }

    public class SingleMessageRequest
    {
        [JsonIgnore]
        public int? SenderId { get; set; }

        [Required(ErrorMessage = "Phone number is required")]
        [Phone(ErrorMessage = "Invalid phone number format")]
        [StringLength(15, MinimumLength = 10, ErrorMessage = "Phone number must be between 10 and 15 digits")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Message text is required")]
        [StringLength(1600, ErrorMessage = "Message cannot exceed 1600 characters")]
        public string Message { get; set; } = string.Empty;

        [StringLength(11, ErrorMessage = "Sender ID cannot exceed 11 characters")]
        public string SenderIdCustom { get; set; } = string.Empty;

        public bool IsPriority { get; set; } = false;

        public MessageType MessageType { get; set; } = MessageType.Promotional;

        [JsonIgnore]
        public string RequestId { get; set; } = string.Empty;
    }

    public class BulkMessageRequest
    {
        [JsonIgnore]
        public int? UserId { get; set; }

        [Required(ErrorMessage = "At least one phone number is required")]
        [MinLength(1, ErrorMessage = "At least one phone number is required")]
        [MaxLength(1000, ErrorMessage = "Maximum of 1000 phone numbers allowed per request")]
        public List<string> PhoneNumbers { get; set; } = new List<string>();

        [Required(ErrorMessage = "Message text is required")]
        [StringLength(1600, ErrorMessage = "Message cannot exceed 1600 characters")]
        public string Message { get; set; } = string.Empty;

        [StringLength(11, ErrorMessage = "Sender ID cannot exceed 11 characters")]
        public string SenderId { get; set; } = string.Empty;

        public bool IsPriority { get; set; } = false;

        public MessageType MessageType { get; set; } = MessageType.Promotional;

        public string CampaignId { get; set; } = string.Empty;

        [JsonIgnore]
        public string RequestId { get; set; } = System.Guid.NewGuid().ToString();

        [JsonIgnore]
        public System.DateTime RequestedAt { get; set; } = System.DateTime.UtcNow;

        [JsonIgnore]
        public string ClientIp { get; set; } = string.Empty;

        [JsonIgnore]
        public string UserAgent { get; set; } = string.Empty;
    }

    public class MessageLogQuery
    {
        public int? UserId { get; set; }
        public System.DateTime? StartDate { get; set; }
        public System.DateTime? EndDate { get; set; }
        public bool? Success { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class MessageSendResult
    {
        public string PhoneNumber { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Response { get; set; } = string.Empty;
        public string MessageId { get; set; } = string.Empty;
        public decimal Cost { get; set; }
    }

    public class RateLimitInfo
    {
        public string Key { get; set; } = string.Empty;
        public int Count { get; set; }
        public System.DateTime FirstRequest { get; set; } = System.DateTime.UtcNow;
        public System.DateTime WindowEnd { get; set; }
        public bool IsExceeded { get; set; }
    }

    public enum MessageType
    {
        Promotional = 1,
        Transactional = 2,
        Alert = 3,
        Marketing = 4
    }

    public enum MessageStatus
    {
        Pending = 1,
        Sent = 2,
        Delivered = 3,
        Failed = 4,
        Expired = 5
    }

    // Custom validation attribute for future dates
    public class FutureDateAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            if (value is DateTime dateTime)
            {
                return dateTime > DateTime.UtcNow;
            }
            return false;
        }
    }
}