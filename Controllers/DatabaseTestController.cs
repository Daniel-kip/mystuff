using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;

namespace DelTechApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseTestController : ControllerBase
    {
        private readonly IConfiguration _config;

        public DatabaseTestController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet("check")]
        public IActionResult CheckDatabaseConnection()
        {
            string connStr = _config.GetConnectionString("MySqlConnection");

            try
            {
                using var conn = new MySqlConnection(connStr);
                conn.Open();

                // Query a simple table to confirm connection
                using var cmd = new MySqlCommand("SHOW DATABASES;", conn);
                using var reader = cmd.ExecuteReader();

                var databases = new List<string>();
                while (reader.Read())
                {
                    databases.Add(reader.GetString(0));
                }

                return Ok(new
                {
                    status = "Connected ",
                    server = conn.DataSource,
                    databases
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    status = "Connection failed ",
                    error = ex.Message
                });
            }
        }
    }
}
