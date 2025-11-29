using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace DelTechApi.Services
{
    public class BackgroundPollingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackgroundPollingService> _logger;
        private readonly IConfiguration _configuration;

        private readonly TimeSpan _keyRotationCheckInterval;
        private readonly TimeSpan _pollingInterval;

        public BackgroundPollingService(
            IServiceScopeFactory scopeFactory,
            ILogger<BackgroundPollingService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;

            // Load from appsettings.json, with fallbacks
            var keyRotationHours = _configuration.GetValue<int?>("Jwt:KeyRotationIntervalHours") ?? 12;
            var pollingMinutes = _configuration.GetValue<int?>("Jwt:PollingIntervalMinutes") ?? 1;

            _keyRotationCheckInterval = TimeSpan.FromHours(keyRotationHours);
            _pollingInterval = TimeSpan.FromMinutes(pollingMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            DateTime lastKeyRotationCheck = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    // --- 1.Database polling logic ---
                    var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    await databaseService.WithConnectionAsync(async connection =>
                    {
                        _logger.LogInformation("Background polling executed successfully at {Time}", DateTime.UtcNow);
                        return Task.CompletedTask;
                    });

                    // --- 2.JWT key rotation check ---
                    if (DateTime.UtcNow - lastKeyRotationCheck >= _keyRotationCheckInterval)
                    {
                        var jwtKeyService = scope.ServiceProvider.GetRequiredService<JwtKeyRotationService>();

                        try
                        {
                            jwtKeyService.InitializeOrRotateKeys();
                            _logger.LogInformation("JWT key rotation check executed successfully at {Time}", DateTime.UtcNow);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error occurred during JWT key rotation check");
                        }

                        lastKeyRotationCheck = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in background polling service");
                }

                // Wait until next polling iteration
                await Task.Delay(_pollingInterval, stoppingToken);
            }
        }
    }
}
