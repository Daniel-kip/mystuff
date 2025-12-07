using MySql.Data.MySqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

// User settings model
public class UserSettings
{
    public int UserId { get; set; }
    public bool Notifications { get; set; }
    public bool EmailUpdates { get; set; }
    public bool SmsAlerts { get; set; }
    public bool DarkMode { get; set; }
    public bool CompactMode { get; set; }
    public string Language { get; set; } = "en";
    public string Currency { get; set; } = "KES";
    public string Timezone { get; set; } = "Africa/Nairobi";
    public string ProfileVisibility { get; set; } = "public";
    public bool SearchEngineIndexing { get; set; }
    public bool DataTracking { get; set; }
    public bool TwoFactorAuth { get; set; }
    public bool LoginAlerts { get; set; }
    public bool ReduceMotion { get; set; }
    public bool HighContrast { get; set; }
    public bool AutoRenew { get; set; }
}

// Service to manage user settings
public class UserSettingsService
{
    private readonly string _connectionString;

    public UserSettingsService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MySqlConnection");
    }

    // Read user settings by user ID
    public async Task<UserSettings?> GetSettingsAsync(int userId)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand("SELECT * FROM user_settings WHERE user_id = @userId", conn);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!reader.Read()) return null;

        // Map DB fields to model, handle nulls
        return new UserSettings
        {
            UserId = userId,
            Notifications = reader["notifications"] != DBNull.Value && Convert.ToBoolean(reader["notifications"]),
            EmailUpdates = reader["email_updates"] != DBNull.Value && Convert.ToBoolean(reader["email_updates"]),
            SmsAlerts = reader["sms_alerts"] != DBNull.Value && Convert.ToBoolean(reader["sms_alerts"]),
            DarkMode = reader["dark_mode"] != DBNull.Value && Convert.ToBoolean(reader["dark_mode"]),
            CompactMode = reader["compact_mode"] != DBNull.Value && Convert.ToBoolean(reader["compact_mode"]),
            Language = reader["language"]?.ToString() ?? "en",
            Currency = reader["currency"]?.ToString() ?? "KES",
            Timezone = reader["timezone"]?.ToString() ?? "Africa/Nairobi",
            ProfileVisibility = reader["profile_visibility"]?.ToString() ?? "public",
            SearchEngineIndexing = reader["search_engine_indexing"] != DBNull.Value && Convert.ToBoolean(reader["search_engine_indexing"]),
            DataTracking = reader["data_tracking"] != DBNull.Value && Convert.ToBoolean(reader["data_tracking"]),
            TwoFactorAuth = reader["two_factor_auth"] != DBNull.Value && Convert.ToBoolean(reader["two_factor_auth"]),
            LoginAlerts = reader["login_alerts"] != DBNull.Value && Convert.ToBoolean(reader["login_alerts"]),
            ReduceMotion = reader["reduce_motion"] != DBNull.Value && Convert.ToBoolean(reader["reduce_motion"]),
            HighContrast = reader["high_contrast"] != DBNull.Value && Convert.ToBoolean(reader["high_contrast"]),
            AutoRenew = reader["auto_renew"] != DBNull.Value && Convert.ToBoolean(reader["auto_renew"])
        };
    }

    // Save or update user settings
    public async Task SaveSettingsAsync(int userId, UserSettings settings)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var cmd = new MySqlCommand(@"
            INSERT INTO user_settings 
            (user_id, notifications, email_updates, sms_alerts, dark_mode, compact_mode, language, currency, timezone, profile_visibility, 
             search_engine_indexing, data_tracking, two_factor_auth, login_alerts, reduce_motion, high_contrast, auto_renew)
            VALUES 
            (@userId, @notifications, @emailUpdates, @smsAlerts, @darkMode, @compactMode, @language, @currency, @timezone, 
             @profileVisibility, @searchEngineIndexing, @dataTracking, @twoFactorAuth, @loginAlerts, 
             @reduceMotion, @highContrast, @autoRenew)
            ON DUPLICATE KEY UPDATE 
                notifications=@notifications,
                email_updates=@emailUpdates,
                sms_alerts=@smsAlerts,
                dark_mode=@darkMode,
                compact_mode=@compactMode,
                language=@language,
                currency=@currency,
                timezone=@timezone,
                profile_visibility=@profileVisibility,
                search_engine_indexing=@searchEngineIndexing,
                data_tracking=@dataTracking,
                two_factor_auth=@twoFactorAuth,
                login_alerts=@loginAlerts,
                reduce_motion=@reduceMotion,
                high_contrast=@highContrast,
                auto_renew=@autoRenew;
        ", conn);

        // Bind parameters with boolean -> int conversion
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@notifications", settings.Notifications ? 1 : 0);
        cmd.Parameters.AddWithValue("@emailUpdates", settings.EmailUpdates ? 1 : 0);
        cmd.Parameters.AddWithValue("@smsAlerts", settings.SmsAlerts ? 1 : 0);
        cmd.Parameters.AddWithValue("@darkMode", settings.DarkMode ? 1 : 0);
        cmd.Parameters.AddWithValue("@compactMode", settings.CompactMode ? 1 : 0);
        cmd.Parameters.AddWithValue("@language", settings.Language);
        cmd.Parameters.AddWithValue("@currency", settings.Currency);
        cmd.Parameters.AddWithValue("@timezone", settings.Timezone);
        cmd.Parameters.AddWithValue("@profileVisibility", settings.ProfileVisibility);
        cmd.Parameters.AddWithValue("@searchEngineIndexing", settings.SearchEngineIndexing ? 1 : 0);
        cmd.Parameters.AddWithValue("@dataTracking", settings.DataTracking ? 1 : 0);
        cmd.Parameters.AddWithValue("@twoFactorAuth", settings.TwoFactorAuth ? 1 : 0);
        cmd.Parameters.AddWithValue("@loginAlerts", settings.LoginAlerts ? 1 : 0);
        cmd.Parameters.AddWithValue("@reduceMotion", settings.ReduceMotion ? 1 : 0);
        cmd.Parameters.AddWithValue("@highContrast", settings.HighContrast ? 1 : 0);
        cmd.Parameters.AddWithValue("@autoRenew", settings.AutoRenew ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }
}
