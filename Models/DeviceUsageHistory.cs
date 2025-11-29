namespace DeviceMonitorAPI.Models
{
    public class DeviceUsageHistory
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public double UsageGB { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
