namespace DelTechISP.Models
{
    public class ContactRequest
    {
        public int Id { get; set; }  // Primary key (auto-generated)
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
