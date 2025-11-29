using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using DeviceMonitorAPI.Models;

namespace DeviceMonitorAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsageController : ControllerBase
    {
        private readonly IDbConnection _connection;

        public UsageController(IDbConnection connection)
        {
            _connection = connection;
        }

        [HttpGet("{deviceId}")]
        public async Task<IActionResult> GetUsage(int deviceId)
        {
            var history = new List<DeviceUsageHistory>();
            using var conn = (MySqlConnection)_connection;
            await conn.OpenAsync();

            var cmd = new MySqlCommand(
                "SELECT id, device_id, usage_gb, timestamp FROM device_usage_history WHERE device_id = @id ORDER BY timestamp DESC",
                conn);
            cmd.Parameters.AddWithValue("@id", deviceId);

            var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                history.Add(new DeviceUsageHistory
                {
                    Id = reader.GetInt32("id"),
                    DeviceId = reader.GetInt32("device_id"),
                    UsageGB = reader.GetDouble("usage_gb"),
                    Timestamp = reader.GetDateTime("timestamp")
                });
            }

            return Ok(history);
        }

        [HttpPost]
        public async Task<IActionResult> AddUsage([FromBody] DeviceUsageHistory usage)
        {
            using var conn = (MySqlConnection)_connection;
            await conn.OpenAsync();

            var cmd = new MySqlCommand(
                "INSERT INTO device_usage_history (device_id, usage_gb, timestamp) VALUES (@device, @usage, @time)",
                conn);
            cmd.Parameters.AddWithValue("@device", usage.DeviceId);
            cmd.Parameters.AddWithValue("@usage", usage.UsageGB);
            cmd.Parameters.AddWithValue("@time", usage.Timestamp);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0 ? Ok("Usage added") : BadRequest("Insert failed");
        }
    }
}
