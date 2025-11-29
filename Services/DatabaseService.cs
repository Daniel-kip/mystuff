using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace DelTechApi.Services
{
    public interface IDatabaseService
    {
        Task<T> WithConnectionAsync<T>(Func<IDbConnection, Task<T>> operation);
        Task WithConnectionAsync(Func<IDbConnection, Task> operation);
    }

    public class DatabaseService : IDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            _connectionString = configuration.GetConnectionString("MySqlConnection") 
                ?? throw new ArgumentNullException("MySQL connection string is missing");
            _logger = logger;
        }

        public async Task<T> WithConnectionAsync<T>(Func<IDbConnection, Task<T>> operation)
        {
            await using var connection = new MySqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync();
                return await operation(connection);
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error occurred");
                throw;
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }

        public async Task WithConnectionAsync(Func<IDbConnection, Task> operation)
        {
            await using var connection = new MySqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync();
                await operation(connection);
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "Database error occurred");
                throw;
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}