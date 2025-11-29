namespace DeviceMonitorAPI.Models
{
    public class Device
    {
        public int Id { get; set; }

        public required string Name { get; set; }

        public required string Type { get; set; }

        public required string Ip { get; set; }

        public string? MacAddress { get; set; }  // optional MAC field

        public string Status { get; set; } = "active";

        public double DataUsedGB { get; set; }

        public DateTime? LastUpdated { get; set; }  // now nullable to match DB

        public string? Meta { get; set; }  // optional metadata JSON/text
    }
}
