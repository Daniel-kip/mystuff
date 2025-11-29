using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace DelTechApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VouchersController : ControllerBase
    {
        private readonly IDbConnection _db;
        public VouchersController(IDbConnection db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            try
            {
                var sql = @"
                    SELECT 
                        1 AS No, 
                        'MJ9APN' AS Code, 
                        'DELTECH HOTSPOT' AS Station, 
                        'BS8FC6' AS Package, 
                        170 AS Amount, 
                        '2547...' AS PaidWith 
                    FROM (SELECT 1) AS t;
                ";

                var rows = await _db.QueryAsync<dynamic>(sql);
                return Ok(rows);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}
