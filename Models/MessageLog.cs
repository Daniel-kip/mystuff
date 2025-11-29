using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DelTechApi.Models
{
    public class MessageLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        [StringLength(20)]
        public string Recipient { get; set; } = string.Empty;

        [Required]
        public string MessageText { get; set; } = string.Empty;

        [StringLength(100)]
        public string? MessageId { get; set; }

        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public string? Response { get; set; }

        public decimal Cost { get; set; }

        public DateTime SentAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? DeviceInfo { get; set; }

        [StringLength(45)]
        public string? IpAddress { get; set; }

        // Helper property for success status
        [JsonIgnore]
        public bool Success => Status == "Delivered" || Status == "Sent";

        // Navigation properties
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
    }
}