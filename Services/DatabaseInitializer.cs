using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using System.Threading.Tasks;

namespace DelTechApi.Services
{
    public class DatabaseInitializer
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseInitializer> _logger;

        public DatabaseInitializer(IConfiguration configuration, ILogger<DatabaseInitializer> logger)
        {
            _connectionString = configuration.GetConnectionString("MySqlConnection");
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            try
            {
                Console.WriteLine("--> Starting Database Initializer...");
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                // Create user_settings table (Simpler, no FK for now to ensure creation)
                var sql = @"
                    CREATE TABLE IF NOT EXISTS user_settings (
                        user_id INT PRIMARY KEY,
                        notifications TINYINT(1) DEFAULT 1,
                        email_updates TINYINT(1) DEFAULT 0,
                        sms_alerts TINYINT(1) DEFAULT 0,
                        dark_mode TINYINT(1) DEFAULT 0,
                        compact_mode TINYINT(1) DEFAULT 0,
                        language VARCHAR(10) DEFAULT 'en',
                        currency VARCHAR(10) DEFAULT 'KES',
                        timezone VARCHAR(50) DEFAULT 'Africa/Nairobi',
                        profile_visibility VARCHAR(20) DEFAULT 'public',
                        search_engine_indexing TINYINT(1) DEFAULT 1,
                        data_tracking TINYINT(1) DEFAULT 0,
                        two_factor_auth TINYINT(1) DEFAULT 0,
                        login_alerts TINYINT(1) DEFAULT 1,
                        reduce_motion TINYINT(1) DEFAULT 0,
                        high_contrast TINYINT(1) DEFAULT 0,
                        auto_renew TINYINT(1) DEFAULT 1
                    );";

                await connection.ExecuteAsync(sql);
                Console.WriteLine("--> Database initialization completed. 'user_settings' table verified.");
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"--> FATAL DB INIT ERROR: {ex.Message}");
                _logger.LogError(ex, "Error initializing database tables.");
            }
        }
    }
}
