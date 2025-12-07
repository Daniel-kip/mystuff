using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Threading.Tasks;

namespace DelTechApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatabaseInitController : ControllerBase
    {
        private readonly IConfiguration _config;

        public DatabaseInitController(IConfiguration config)
        {
            _config = config;
        }

        [HttpPost("initialize")]
        public async Task<IActionResult> InitializeDatabase()
        {
            string connStr = _config.GetConnectionString("MySqlConnection");
            string sqlFilePath = Path.Combine(Directory.GetCurrentDirectory(), "SQL", "setup_database.sql");

            if (!System.IO.File.Exists(sqlFilePath))
                return NotFound(new { status = "SQL file not found", path = sqlFilePath });

            try
            {
                string script = await System.IO.File.ReadAllTextAsync(sqlFilePath);

                using var conn = new MySqlConnection(connStr);
                await conn.OpenAsync();

                using var cmd = new MySqlCommand(script, conn);
                cmd.CommandTimeout = 300; // 5 minutes in case of large scripts
                await cmd.ExecuteNonQueryAsync();

                return Ok(new { status = "Database initialized successfully!" });
            }
            catch (MySqlException ex)
            {
                return StatusCode(500, new { status = "Database initialization failed", error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { status = "Unexpected error", error = ex.Message });
            }
        }
    }
}
