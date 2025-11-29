namespace DeviceMonitorAPI.Models
{
    public class UsageSummary
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public double TotalGB { get; set; }
    }
}
