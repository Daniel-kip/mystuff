using DelTechApi.Models;
using Microsoft.Extensions.Logging;
using System.Text;
using Dapper;

namespace DelTechApi.Services
{
    public interface IMessageLogService
    {
        Task<MessageLog> AddLogAsync(MessageLog log);
        Task<IEnumerable<MessageLog>> GetLogsAsync(MessageLogQuery query);
        Task<MessageLog?> GetLogByIdAsync(int id);
        Task<bool> UpdateLogAsync(MessageLog log);
        Task<bool> DeleteLogAsync(int id);
        Task<int> GetLogsCountAsync(MessageLogQuery query);
        Task<IEnumerable<MessageStat>> GetMessageStatsAsync(MessageStatsQuery query);
        Task<bool> CleanupOldLogsAsync(int daysToKeep = 90);
        Task<IEnumerable<MessageLog>> GetRecentLogsByUserAsync(int userId, int count = 10);
        Task<decimal> GetTotalCostAsync(MessageLogQuery query);
    }

    public class MessageLogService : IMessageLogService
    {
        private readonly IDatabaseService _databaseService;
        private readonly ILogger<MessageLogService> _logger;

        public MessageLogService(IDatabaseService databaseService, ILogger<MessageLogService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        public async Task<MessageLog> AddLogAsync(MessageLog log)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = @"INSERT INTO message_logs 
                            (user_id, recipient, message_text, message_id, status, response, 
                             cost, sent_at, created_at, device_info, ip_address)
                            VALUES (@UserId, @Recipient, @MessageText, @MessageId, @Status, @Response,
                                   @Cost, @SentAt, @CreatedAt, @DeviceInfo, @IpAddress);
                            SELECT LAST_INSERT_ID();";

                var parameters = new
                {
                    UserId = log.UserId,
                    Recipient = log.Recipient,
                    MessageText = log.MessageText,
                    MessageId = log.MessageId,
                    Status = log.Status ?? (log.Success ? "Delivered" : "Failed"),
                    Response = log.Response,
                    Cost = log.Cost,
                    SentAt = log.SentAt,
                    CreatedAt = DateTime.UtcNow,
                    DeviceInfo = log.DeviceInfo,
                    IpAddress = log.IpAddress
                };

                var logId = await connection.ExecuteScalarAsync<int>(sql, parameters);
                log.Id = logId;

                _logger.LogInformation("Message log added with ID: {LogId} for user {UserId}", logId, log.UserId);
                return log;
            });
        }

        public async Task<IEnumerable<MessageLog>> GetLogsAsync(MessageLogQuery query)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = new StringBuilder(@"
                    SELECT ml.*, u.full_name as UserName, u.email as UserEmail
                    FROM message_logs ml
                    LEFT JOIN auth_users u ON ml.user_id = u.id
                    WHERE 1=1");

                var parameters = new DynamicParameters();

                if (query.UserId.HasValue)
                {
                    sql.Append(" AND ml.user_id = @UserId");
                    parameters.Add("UserId", query.UserId.Value);
                }

                if (query.StartDate.HasValue)
                {
                    sql.Append(" AND ml.sent_at >= @StartDate");
                    parameters.Add("StartDate", query.StartDate.Value);
                }

                if (query.EndDate.HasValue)
                {
                    sql.Append(" AND ml.sent_at <= @EndDate");
                    parameters.Add("EndDate", query.EndDate.Value);
                }

                if (query.Success.HasValue)
                {
                    sql.Append(" AND ml.status = @Status");
                    parameters.Add("Status", query.Success.Value ? "Delivered" : "Failed");
                }

                if (!string.IsNullOrEmpty(query.PhoneNumber))
                {
                    sql.Append(" AND ml.recipient LIKE @PhoneNumber");
                    parameters.Add("PhoneNumber", $"%{query.PhoneNumber}%");
                }

                if (!string.IsNullOrEmpty(query.MessageId))
                {
                    sql.Append(" AND ml.message_id = @MessageId");
                    parameters.Add("MessageId", query.MessageId);
                }

                sql.Append(" ORDER BY ml.sent_at DESC");

                if (query.PageSize > 0)
                {
                    sql.Append(" LIMIT @PageSize OFFSET @Offset");
                    parameters.Add("PageSize", query.PageSize);
                    parameters.Add("Offset", (query.Page - 1) * query.PageSize);
                }

                return await connection.QueryAsync<MessageLog>(sql.ToString(), parameters);
            });
        }

        public async Task<MessageLog?> GetLogByIdAsync(int id)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = @"
                    SELECT ml.*, u.full_name as UserName, u.email as UserEmail
                    FROM message_logs ml
                    LEFT JOIN auth_users u ON ml.user_id = u.id
                    WHERE ml.id = @Id";

                return await connection.QueryFirstOrDefaultAsync<MessageLog>(sql, new { Id = id });
            });
        }

        public async Task<bool> UpdateLogAsync(MessageLog log)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = @"
                    UPDATE message_logs 
                    SET status = @Status, 
                        response = @Response,
                        cost = @Cost,
                        message_id = @MessageId,
                        sent_at = @SentAt
                    WHERE id = @Id";

                var parameters = new
                {
                    Status = log.Status,
                    Response = log.Response,
                    Cost = log.Cost,
                    MessageId = log.MessageId,
                    SentAt = log.SentAt,
                    Id = log.Id
                };

                var affectedRows = await connection.ExecuteAsync(sql, parameters);
                var success = affectedRows > 0;

                if (success)
                {
                    _logger.LogInformation("Message log updated with ID: {LogId}", log.Id);
                }

                return success;
            });
        }

        public async Task<bool> DeleteLogAsync(int id)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = "DELETE FROM message_logs WHERE id = @Id";
                var affectedRows = await connection.ExecuteAsync(sql, new { Id = id });
                var success = affectedRows > 0;

                if (success)
                {
                    _logger.LogInformation("Message log deleted with ID: {LogId}", id);
                }

                return success;
            });
        }

        public async Task<int> GetLogsCountAsync(MessageLogQuery query)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = new StringBuilder("SELECT COUNT(*) FROM message_logs ml WHERE 1=1");
                var parameters = new DynamicParameters();

                if (query.UserId.HasValue)
                {
                    sql.Append(" AND ml.user_id = @UserId");
                    parameters.Add("UserId", query.UserId.Value);
                }

                if (query.StartDate.HasValue)
                {
                    sql.Append(" AND ml.sent_at >= @StartDate");
                    parameters.Add("StartDate", query.StartDate.Value);
                }

                if (query.EndDate.HasValue)
                {
                    sql.Append(" AND ml.sent_at <= @EndDate");
                    parameters.Add("EndDate", query.EndDate.Value);
                }

                if (query.Success.HasValue)
                {
                    sql.Append(" AND ml.status = @Status");
                    parameters.Add("Status", query.Success.Value ? "Delivered" : "Failed");
                }

                if (!string.IsNullOrEmpty(query.PhoneNumber))
                {
                    sql.Append(" AND ml.recipient LIKE @PhoneNumber");
                    parameters.Add("PhoneNumber", $"%{query.PhoneNumber}%");
                }

                return await connection.ExecuteScalarAsync<int>(sql.ToString(), parameters);
            });
        }

        public async Task<IEnumerable<MessageStat>> GetMessageStatsAsync(MessageStatsQuery query)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = @"
                    SELECT 
                        DATE(sent_at) as Date,
                        COUNT(*) as TotalMessages,
                        SUM(CASE WHEN status = 'Delivered' THEN 1 ELSE 0 END) as SuccessfulMessages,
                        SUM(CASE WHEN status != 'Delivered' THEN 1 ELSE 0 END) as FailedMessages,
                        SUM(cost) as TotalCost,
                        user_id as UserId
                    FROM message_logs 
                    WHERE sent_at >= @StartDate AND sent_at <= @EndDate";

                var parameters = new DynamicParameters();
                parameters.Add("StartDate", query.StartDate);
                parameters.Add("EndDate", query.EndDate);

                if (query.UserId.HasValue)
                {
                    sql += " AND user_id = @UserId";
                    parameters.Add("UserId", query.UserId.Value);
                }

                sql += " GROUP BY DATE(sent_at), user_id ORDER BY Date DESC";

                return await connection.QueryAsync<MessageStat>(sql, parameters);
            });
        }

        public async Task<bool> CleanupOldLogsAsync(int daysToKeep = 90)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);
                var sql = "DELETE FROM message_logs WHERE sent_at < @CutoffDate";
                
                var affectedRows = await connection.ExecuteAsync(sql, new { CutoffDate = cutoffDate });
                
                _logger.LogInformation("Cleaned up {Count} old message logs older than {CutoffDate}", 
                    affectedRows, cutoffDate.ToString("yyyy-MM-dd"));
                
                return affectedRows > 0;
            });
        }

        public async Task<IEnumerable<MessageLog>> GetRecentLogsByUserAsync(int userId, int count = 10)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = @"
                    SELECT * FROM message_logs 
                    WHERE user_id = @UserId 
                    ORDER BY sent_at DESC 
                    LIMIT @Count";

                return await connection.QueryAsync<MessageLog>(sql, new { UserId = userId, Count = count });
            });
        }

        public async Task<decimal> GetTotalCostAsync(MessageLogQuery query)
        {
            return await _databaseService.WithConnectionAsync(async connection =>
            {
                var sql = new StringBuilder("SELECT SUM(cost) FROM message_logs WHERE 1=1");
                var parameters = new DynamicParameters();

                if (query.UserId.HasValue)
                {
                    sql.Append(" AND user_id = @UserId");
                    parameters.Add("UserId", query.UserId.Value);
                }

                if (query.StartDate.HasValue)
                {
                    sql.Append(" AND sent_at >= @StartDate");
                    parameters.Add("StartDate", query.StartDate.Value);
                }

                if (query.EndDate.HasValue)
                {
                    sql.Append(" AND sent_at <= @EndDate");
                    parameters.Add("EndDate", query.EndDate.Value);
                }

                if (query.Success.HasValue)
                {
                    sql.Append(" AND status = @Status");
                    parameters.Add("Status", query.Success.Value ? "Delivered" : "Failed");
                }

                var result = await connection.ExecuteScalarAsync<decimal?>(sql.ToString(), parameters);
                return result ?? 0;
            });
        }
    }

    // Supporting models
    public class MessageStatsQuery
    {
        public DateTime StartDate { get; set; } = DateTime.UtcNow.AddDays(-30);
        public DateTime EndDate { get; set; } = DateTime.UtcNow;
        public int? UserId { get; set; }
    }

    public class MessageStat
    {
        public DateTime Date { get; set; }
        public int TotalMessages { get; set; }
        public int SuccessfulMessages { get; set; }
        public int FailedMessages { get; set; }
        public decimal TotalCost { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
    }
}