using Microsoft.Extensions.Logging;

namespace DelTechApi.Services
{
    public class SmsService
    {
        private readonly IMessageLogService _logService;
        private readonly ILogger<SmsService> _logger;

        public SmsService(IMessageLogService logService, ILogger<SmsService> logger)
        {
            _logService = logService;
            _logger = logger;
        }

        // Add SMS-related methods here
    }
}