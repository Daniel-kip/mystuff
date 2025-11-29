using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using DeviceMonitorAPI.Models;

namespace DeviceMonitorAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DevicesController : ControllerBase
    {
        private readonly IDbConnection _connection;

        public DevicesController(IDbConnection connection)
        {
            _connection = connection;
        }

        [HttpGet]
        public async Task<IActionResult> GetDevices()
        {
            var devices = new List<ConnectedDevice>();
            using var conn = (MySqlConnection)_connection;
            await conn.OpenAsync();

            var cmd = new MySqlCommand("SELECT * FROM connected_devices ORDER BY created_at DESC", conn);
            var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                devices.Add(new ConnectedDevice
                {
                    Id = reader.GetInt32("id"),
                    DeviceName = reader.GetString("device_name"),
                    DeviceType = reader.GetString("device_type"),
                    IpAddress = reader["ip_address"] as string,
                    MacAddress = reader["mac_address"] as string,
                    Status = reader["status"]?.ToString() ?? "Unknown",
                    OwnerId = reader["owner_id"] as int?,
                    CreatedAt = reader.GetDateTime("created_at")
                });
            }

            return Ok(devices);
        }

        [HttpPost]
        public async Task<IActionResult> AddDevice([FromBody] ConnectedDevice device)
        {
            using var conn = (MySqlConnection)_connection;
            await conn.OpenAsync();

            var cmd = new MySqlCommand(@"
                INSERT INTO connected_devices 
                (device_name, device_type, ip_address, mac_address, status, owner_id) 
                VALUES (@name, @type, @ip, @mac, @status, @owner)", conn);

            cmd.Parameters.AddWithValue("@name", device.DeviceName);
            cmd.Parameters.AddWithValue("@type", device.DeviceType);
            cmd.Parameters.AddWithValue("@ip", device.IpAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@mac", device.MacAddress ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@status", device.Status);
            cmd.Parameters.AddWithValue("@owner", device.OwnerId ?? (object)DBNull.Value);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0 ? Ok("Device added") : BadRequest("Insert failed");
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDevice(int id)
        {
            using var conn = (MySqlConnection)_connection;
            await conn.OpenAsync();

            var cmd = new MySqlCommand("DELETE FROM connected_devices WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0 ? Ok("Device deleted") : NotFound("Device not found");
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
        {
            using var conn = (MySqlConnection)_connection;
            await conn.OpenAsync();

            var cmd = new MySqlCommand("UPDATE connected_devices SET status = @status WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@id", id);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0 ? Ok("Status updated") : NotFound("Device not found");
        }
    }
}
