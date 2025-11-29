using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DelTechApi.Services
{
    public class DatabaseMaintenanceService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DatabaseMaintenanceService> _logger;

        public DatabaseMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<DatabaseMaintenanceService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var logService = scope.ServiceProvider.GetRequiredService<IMessageLogService>();
                        
                        // Cleanup logs older than 90 days
                        await logService.CleanupOldLogsAsync(90);
                        _logger.LogInformation("Database maintenance completed");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in database maintenance");
                }

                // Run once per day
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }
    }
}