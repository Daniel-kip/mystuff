using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace DelTechApi.Services
{
    public class JwtKeyRotationService
    {
        private readonly IDataProtector _protector;
        private readonly string _activeKeyPath;
        private readonly string _previousKeyPath;
        private readonly string _archiveKeyPath;

        private byte[]? _activeKeyBytes;
        private byte[]? _previousKeyBytes;

        public JwtKeyRotationService(IDataProtector protector, string activeKeyPath, string previousKeyPath, string archiveKeyPath)
        {
            _protector = protector;
            _activeKeyPath = activeKeyPath;
            _previousKeyPath = previousKeyPath;
            _archiveKeyPath = archiveKeyPath;
        }

        /// <summary>
        /// Load existing keys or rotate if necessary.
        /// </summary>
        public void InitializeOrRotateKeys()
        {
            var keyLifetime = TimeSpan.FromDays(30);

            if (!File.Exists(_activeKeyPath) || ShouldRotateKey(_activeKeyPath, keyLifetime))
            {
                RotateKeys();
            }
            else
            {
                LoadExistingKeys();
            }
        }

        private bool ShouldRotateKey(string keyPath, TimeSpan maxLifetime)
        {
            var fileInfo = new FileInfo(keyPath);
            return DateTime.UtcNow - fileInfo.LastWriteTimeUtc > maxLifetime;
        }

        private void RotateKeys()
        {
            // Archive previous key
            if (File.Exists(_previousKeyPath))
            {
                File.WriteAllBytes(_archiveKeyPath, File.ReadAllBytes(_previousKeyPath));
            }

            // Move active → previous
            if (File.Exists(_activeKeyPath))
            {
                var previousProtected = File.ReadAllBytes(_activeKeyPath);
                File.WriteAllBytes(_previousKeyPath, previousProtected);

                try
                {
                    _previousKeyBytes = _protector.Unprotect(previousProtected);
                }
                catch
                {
                    _previousKeyBytes = null;
                }
            }

            // Generate new active key
            var newKey = RandomNumberGenerator.GetBytes(64);
            File.WriteAllBytes(_activeKeyPath, _protector.Protect(newKey));
            _activeKeyBytes = newKey;
        }

        private void LoadExistingKeys()
        {
            // Load active key
            _activeKeyBytes = _protector.Unprotect(File.ReadAllBytes(_activeKeyPath));

            // Load previous key (if exists)
            if (File.Exists(_previousKeyPath))
            {
                try
                {
                    _previousKeyBytes = _protector.Unprotect(File.ReadAllBytes(_previousKeyPath));
                }
                catch
                {
                    _previousKeyBytes = null;
                }
            }
        }

        /// <summary>
        /// Returns raw byte arrays for JWT signing.
        /// </summary>
        public IEnumerable<byte[]> GetValidRawKeys()
        {
            if (_activeKeyBytes != null)
                yield return _activeKeyBytes;

            if (_previousKeyBytes != null)
                yield return _previousKeyBytes;
        }

        /// <summary>
        /// Converts raw byte keys to SymmetricSecurityKey for JWT validation.
        /// </summary>
        public IEnumerable<SymmetricSecurityKey> GetValidSigningKeys()
        {
            foreach (var key in GetValidRawKeys())
            {
                yield return new SymmetricSecurityKey(key);
            }
        }

        /// <summary>
        /// Check if there is an active key.
        /// </summary>
        public bool HasActiveKey => _activeKeyBytes != null;
    }

    // Health Check integration
    public class JwtKeyRotationHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
    {
        public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return JwtKeyRotationServiceStatic.ActiveKeyExists
                ? Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("JWT Key Rotation is healthy"))
                : Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("JWT Active Key is missing"));
        }
    }

    // Static helper to track active key health without holding SymmetricSecurityKey statically
    public static class JwtKeyRotationServiceStatic
    {
        public static bool ActiveKeyExists { get; set; }
    }
}
