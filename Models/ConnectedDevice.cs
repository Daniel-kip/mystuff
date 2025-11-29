namespace DeviceMonitorAPI.Models
{
    public class ConnectedDevice
    {
        public int Id { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? MacAddress { get; set; }
        public string Status { get; set; } = "Active";
        public int? OwnerId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
