using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Data;
using System.Net;
using System.Net.Mail;
using DelTechISP.Models;

namespace DelTechISP.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ContactController : ControllerBase
    {
        private readonly IDbConnection _dbConnection;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ContactController> _logger;

        public ContactController(IDbConnection dbConnection, IConfiguration configuration, ILogger<ContactController> logger)
        {
            _dbConnection = dbConnection;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitContact([FromBody] ContactRequest contact)
        {
            if (contact == null)
                return BadRequest("Invalid contact data.");

            try
            {
                // Save to database
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

                // Send email notification
                await SendEmailNotification(contact);

                return Ok(new { success = true, message = "Contact request saved successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing contact form");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        private async Task SendEmailNotification(ContactRequest contact)
        {
            try
            {
                var smtpHost = _configuration["Email:SmtpHost"];
                var smtpPort = int.Parse(_configuration["Email:SmtpPort"] ?? "587");
                var fromEmail = _configuration["Email:FromEmail"];
                var toEmail = _configuration["Email:ToEmail"] ?? "ottydan008@gmail.com";
                var username = _configuration["Email:Username"];
                var password = _configuration["Email:Password"];

                if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("Email configuration incomplete, skipping email notification");
                    return;
                }

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail ?? toEmail, "DelTech Contact Form"),
                    Subject = $"New Contact Form Message from {contact.Name}",
                    Body = $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <h2>New Contact Form Submission</h2>
    <p><strong>Name:</strong> {contact.Name}</p>
    <p><strong>Email:</strong> {contact.Email}</p>
    <p><strong>Phone:</strong> {contact.Phone ?? "Not provided"}</p>
    <hr>
    <h3>Message:</h3>
    <p>{contact.Message}</p>
    <hr>
    <p style='color: #666; font-size: 12px;'>Sent from DelTech Networks Contact Form</p>
</body>
</html>",
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);
                mailMessage.ReplyToList.Add(new MailAddress(contact.Email, contact.Name));

                using var smtpClient = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(username ?? fromEmail, password),
                    EnableSsl = true
                };

                await smtpClient.SendMailAsync(mailMessage);

                _logger.LogInformation("Contact form email sent successfully to {ToEmail}", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send contact email notification");
                // Don't throw - we still saved to database
            }
        }
    }
}
