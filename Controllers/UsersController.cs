using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace DelTechApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IDbConnection _db;
        public UsersController(IDbConnection db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var sql = "SELECT id, name, balance FROM `users`";
                var items = await _db.QueryAsync<User>(sql);
                return Ok(items);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UserCreateDto dto)
        {
            var sql = "INSERT INTO `users` (name, balance) VALUES (@Name, @Balance)";
            await _db.ExecuteAsync(sql, new { Name = dto.Name, Balance = dto.Balance });
            return Created("", dto);
        }
    }

    public record User(int Id, string Name, decimal Balance);
    public record UserCreateDto(string Name, decimal Balance);
}
