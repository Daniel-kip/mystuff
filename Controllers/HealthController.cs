using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;

namespace DeviceMonitorAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IDbConnection _connection;

        public HealthController(IDbConnection connection)
        {
            _connection = connection;
        }

        [HttpGet("db")]
        public IActionResult CheckDatabase()
        {
            try
            {
                _connection.Open();
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "SELECT 1";
                var result = cmd.ExecuteScalar();
                _connection.Close();

                return Ok(new
                {
                    status = "Database connection successful ",
                    result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    status = "Database connection failed ",
                    error = ex.Message
                });
            }
        }
    }
}
