using DelTechApi.Models;
using Microsoft.Extensions.Logging;
using Dapper;

namespace DelTechApi.Services
{
    public class UserService
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<UserService> _logger;

        public UserService(IDatabaseService databaseService, ILogger<UserService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        // Add user-related methods here
        public async Task<object?> GetUserProfileAsync(int userId)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var user = await connection.QueryFirstOrDefaultAsync<object>(
                    "SELECT ID, FULL_NAME, EMAIL, ROLE, CREATED_AT FROM AUTH_USERS WHERE ID = @UserId",
                    new { UserId = userId });

                return user;
            });
        }
    }
}