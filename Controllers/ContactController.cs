using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using DelTechISP.Models;

namespace DelTechISP.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactController : ControllerBase
    {
        private readonly IDbConnection _dbConnection;

        public ContactController(IDbConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        [HttpPost("submit")]
        public IActionResult SubmitContact([FromBody] ContactRequest contact)
        {
            if (contact == null)
                return BadRequest("Invalid contact data.");

            try
            {
                using var conn = (MySqlConnection)_dbConnection;
                conn.Open();

                string sql = @"INSERT INTO contact_requests 
                               (name, email, phone, message, created_at) 
                               VALUES (@Name, @Email, @Phone, @Message, @CreatedAt)";

                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Name", contact.Name);
                cmd.Parameters.AddWithValue("@Email", contact.Email);
                cmd.Parameters.AddWithValue("@Phone", contact.Phone);
                cmd.Parameters.AddWithValue("@Message", contact.Message);
                cmd.Parameters.AddWithValue("@CreatedAt", contact.CreatedAt);

                cmd.ExecuteNonQuery();

                return Ok(new { success = true, message = "Contact request saved successfully!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
