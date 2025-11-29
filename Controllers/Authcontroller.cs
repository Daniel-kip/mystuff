using System;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using DelTechApi.Services;
using Microsoft.Extensions.Logging;

namespace DelTechApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IDatabaseService _databaseService;
        private readonly JwtKeyRotationService _jwtKeyService;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;
        private const int RefreshTokenDays = 30;

        public AuthController(
            IDatabaseService databaseService,
            JwtKeyRotationService jwtKeyService,
            ILogger<AuthController> logger,
            IConfiguration configuration)
        {
            _databaseService = databaseService;
            _jwtKeyService = jwtKeyService;
            _logger = logger;
            _configuration = configuration;
        }

        // GET: api/Auth/Test
        [HttpGet("Test")]
        public IActionResult Test()
        {
            return Ok(new { 
                success = true, 
                message = "Auth API is working", 
                timestamp = DateTime.UtcNow 
            });
        }

        // POST: api/Auth/Register
        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.FullName) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password) ||
                    string.IsNullOrWhiteSpace(request.ConfirmPassword))
                    return BadRequest(new { success = false, message = "All fields are required." });

                if (request.Password != request.ConfirmPassword)
                    return BadRequest(new { success = false, message = "Passwords do not match." });

                if (!IsValidPassword(request.Password))
                    return BadRequest(new { success = false, message = "Password must be at least 8 characters with uppercase, lowercase, number and special character." });

                var emailLower = request.Email.Trim().ToLower();

                var existingUser = await _databaseService.WithConnectionAsync(async connection =>
                    await connection.QueryFirstOrDefaultAsync<int?>(
                        "SELECT ID FROM auth_users WHERE EMAIL = @Email", new { Email = emailLower }));

                if (existingUser != null)
                    return Conflict(new { success = false, message = "Email already registered." });

                var passwordHash = HashPassword(request.Password, out string salt);

                var userId = await _databaseService.WithConnectionAsync(async connection =>
                    await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO auth_users (full_name, email, password_hash, salt, role, created_at)
                        VALUES (@FullName, @Email, @PasswordHash, @Salt, @Role, @CreatedAt);
                        SELECT LAST_INSERT_ID();",
                        new
                        {
                            request.FullName,
                            Email = emailLower,
                            PasswordHash = passwordHash,
                            Salt = salt,
                            Role = request.Role ?? "User",
                            CreatedAt = DateTime.UtcNow
                        }));

                _logger.LogInformation("User registered successfully: {Email} with ID: {UserId}", emailLower, userId);

                return Ok(new { success = true, message = "Registration successful." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new { success = false, message = "An error occurred during registration." });
            }
        }

        // POST: api/Auth/Login
        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                    return BadRequest(new { success = false, message = "Email and password are required." });

                var emailLower = request.Email.Trim().ToLower();
                
                var user = await _databaseService.WithConnectionAsync(async connection =>
                    await connection.QueryFirstOrDefaultAsync<AppUser>(
                        "SELECT * FROM auth_users WHERE EMAIL = @Email", 
                        new { Email = emailLower }));

                if (user == null || !VerifyPassword(request.Password, user.PASSWORD_HASH, user.SALT))
                    return Unauthorized(new { success = false, message = "Invalid credentials." });

                var accessToken = GenerateJwtToken(user, out var accessExpiryUtc);
                var refreshToken = GenerateRefreshToken();
                var refreshExpiry = DateTime.UtcNow.AddDays(RefreshTokenDays);
                var refreshHash = HashToken(refreshToken);

                await _databaseService.WithConnectionAsync(async connection =>
                {
                    await connection.ExecuteAsync(@"
                        INSERT INTO auth_refresh_tokens (token_hash, user_id, expires_at, created_at, revoked, device, ip)
                        VALUES (@TokenHash, @UserId, @ExpiresAt, @CreatedAt, 0, @Device, @Ip)",
                        new
                        {
                            TokenHash = refreshHash,
                            UserId = user.ID,
                            ExpiresAt = refreshExpiry,
                            CreatedAt = DateTime.UtcNow,
                            Device = Request.Headers.UserAgent.ToString() ?? "Unknown",
                            Ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                        });

                    // Cleanup old refresh tokens for this user
                    await connection.ExecuteAsync(
                        "DELETE FROM auth_refresh_tokens WHERE user_id = @UserId AND (expires_at < @Now OR revoked = 1)",
                        new { UserId = user.ID, Now = DateTime.UtcNow.AddDays(-1) });
                });

                SetRefreshCookie(refreshToken, refreshExpiry);

                _logger.LogInformation("Successful login for user: {Email}", emailLower);

                return Ok(new
                {
                    success = true,
                    message = "Login successful",
                    accessToken,
                    accessTokenExpiresAt = accessExpiryUtc.ToString("o"),
                    user = new { user.ID, user.FULL_NAME, user.EMAIL, user.ROLE }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new { success = false, message = "An error occurred during login." });
            }
        }

        // POST: api/Auth/refresh-token
        [HttpPost("refresh-token")]
        public async Task<IActionResult> Refresh()
        {
            try
            {
                var refreshToken = GetRefreshTokenFromCookie();
                if (string.IsNullOrEmpty(refreshToken))
                    return Unauthorized(new { success = false, message = "Refresh token required" });

                var tokenHash = HashToken(refreshToken);
                
                var refreshTokenRecord = await _databaseService.WithConnectionAsync(async connection =>
                    await connection.QueryFirstOrDefaultAsync<RefreshToken>(
                        @"SELECT rt.*, u.* FROM auth_refresh_tokens rt 
                          INNER JOIN auth_users u ON rt.user_id = u.id 
                          WHERE rt.token_hash = @TokenHash AND rt.revoked = 0",
                        new { TokenHash = tokenHash }));

                if (refreshTokenRecord == null || refreshTokenRecord.EXPIRES_AT < DateTime.UtcNow)
                {
                    ClearRefreshCookie();
                    return Unauthorized(new { success = false, message = "Invalid or expired refresh token" });
                }

                var user = new AppUser
                {
                    ID = refreshTokenRecord.USER_ID,
                    FULL_NAME = refreshTokenRecord.FULL_NAME,
                    EMAIL = refreshTokenRecord.EMAIL,
                    ROLE = refreshTokenRecord.ROLE
                };

                var newAccessToken = GenerateJwtToken(user, out var accessExpiryUtc);
                var newRefreshToken = GenerateRefreshToken();
                var newRefreshExpiry = DateTime.UtcNow.AddDays(RefreshTokenDays);
                var newRefreshHash = HashToken(newRefreshToken);

                await _databaseService.WithConnectionAsync(async connection =>
                {
                    await connection.ExecuteAsync(@"
                        UPDATE auth_refresh_tokens SET revoked = 1 WHERE token_hash = @OldTokenHash;
                        INSERT INTO auth_refresh_tokens (token_hash, user_id, expires_at, created_at, revoked, device, ip)
                        VALUES (@NewTokenHash, @UserId, @ExpiresAt, @CreatedAt, 0, @Device, @Ip)",
                        new
                        {
                            OldTokenHash = tokenHash,
                            NewTokenHash = newRefreshHash,
                            UserId = user.ID,
                            ExpiresAt = newRefreshExpiry,
                            CreatedAt = DateTime.UtcNow,
                            Device = Request.Headers.UserAgent.ToString() ?? "Unknown",
                            Ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
                        });
                });

                SetRefreshCookie(newRefreshToken, newRefreshExpiry);

                return Ok(new
                {
                    success = true,
                    accessToken = newAccessToken,
                    accessTokenExpiresAt = accessExpiryUtc.ToString("o"),
                    user = new { user.ID, user.FULL_NAME, user.EMAIL, user.ROLE }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return StatusCode(500, new { success = false, message = "An error occurred during token refresh." });
            }
        }

        // POST: api/Auth/logout
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var cookieToken = GetRefreshTokenFromCookie();
                
                if (!string.IsNullOrEmpty(cookieToken))
                {
                    var tokenHash = HashToken(cookieToken);
                    await _databaseService.WithConnectionAsync(async connection =>
                        await connection.ExecuteAsync(
                            "UPDATE auth_refresh_tokens SET revoked = 1 WHERE token_hash = @TokenHash OR user_id = @UserId",
                            new { TokenHash = tokenHash, UserId = userId }));
                }

                ClearRefreshCookie();
                _logger.LogInformation("User {UserId} logged out", userId);

                return Ok(new { success = true, message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return StatusCode(500, new { success = false, message = "An error occurred during logout." });
            }
        }

        // GET: api/Auth/Verify
        [Authorize]
        [HttpGet("Verify")]
        public IActionResult Verify()
        {
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var name = User.FindFirst(ClaimTypes.Name)?.Value;

            return Ok(new
            {
                success = true,
                message = "Token is valid",
                user = new { id, email, role, name }
            });
        }

        // ===================== Helper Methods =====================

        private string GenerateJwtToken(AppUser user, out DateTime expiresAtUtc)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.ID.ToString()),
                new Claim(ClaimTypes.Email, user.EMAIL),
                new Claim(ClaimTypes.Name, user.FULL_NAME),
                new Claim(ClaimTypes.Role, user.ROLE),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var issuer = _configuration["Jwt:Issuer"] ?? "DelTechAPI";
            var audience = _configuration["Jwt:Audience"] ?? "DelTechClients";

            // Convert raw keys to SymmetricSecurityKey
            var signingKey = _jwtKeyService.GetValidRawKeys()
                .Select(k => new SymmetricSecurityKey(k))
                .First(); // Take the first valid key

            var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
            var now = DateTime.UtcNow;
            expiresAtUtc = now.AddHours(8);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: now,
                expires: expiresAtUtc,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GenerateRefreshToken(int size = 64)
        {
            var randomBytes = new byte[size];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        private static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(token);
            var hashed = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hashed);
        }

        private void SetRefreshCookie(string refreshToken, DateTime expires)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = !_configuration.GetValue<bool>("DisableHttpsRedirection") && 
                         (Request.IsHttps || HttpContext.Request.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)),
                SameSite = SameSiteMode.Strict,
                Expires = expires,
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
        }

        private string? GetRefreshTokenFromCookie()
        {
            return Request.Cookies.TryGetValue("refreshToken", out var value) ? value : null;
        }

        private void ClearRefreshCookie()
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = !_configuration.GetValue<bool>("DisableHttpsRedirection") && 
                         (Request.IsHttps || HttpContext.Request.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)),
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(-1),
                Path = "/"
            };
            Response.Cookies.Append("refreshToken", "", cookieOptions);
        }

        private string HashPassword(string password, out string salt)
        {
            var saltBytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            salt = Convert.ToBase64String(saltBytes);
            using var deriveBytes = new Rfc2898DeriveBytes(password, saltBytes, 10000, HashAlgorithmName.SHA256);
            return Convert.ToBase64String(deriveBytes.GetBytes(32));
        }

        private bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            using var deriveBytes = new Rfc2898DeriveBytes(password, saltBytes, 10000, HashAlgorithmName.SHA256);
            var hash = Convert.ToBase64String(deriveBytes.GetBytes(32));
            return hash == storedHash;
        }

        private bool IsValidPassword(string password)
        {
            return password.Length >= 8 &&
                   password.Any(char.IsUpper) &&
                   password.Any(char.IsLower) &&
                   password.Any(char.IsDigit) &&
                   password.Any(ch => !char.IsLetterOrDigit(ch));
        }

        // ===================== Models =====================

        public class RegisterRequest
        {
            public string FullName { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string ConfirmPassword { get; set; } = string.Empty;
            public string Role { get; set; } = "User";
        }

        public class LoginRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class AppUser
        {
            public int ID { get; set; }
            public string FULL_NAME { get; set; } = string.Empty;
            public string EMAIL { get; set; } = string.Empty;
            public string PASSWORD_HASH { get; set; } = string.Empty;
            public string SALT { get; set; } = string.Empty;
            public string ROLE { get; set; } = "User";
        }

        public class RefreshToken
        {
            public int ID { get; set; }
            public string TOKEN_HASH { get; set; } = string.Empty;
            public int USER_ID { get; set; }
            public DateTime EXPIRES_AT { get; set; }
            public DateTime CREATED_AT { get; set; }
            public bool REVOKED { get; set; }
            public string DEVICE { get; set; } = string.Empty;
            public string IP { get; set; } = string.Empty;
            public string FULL_NAME { get; set; } = string.Empty;
            public string EMAIL { get; set; } = string.Empty;
            public string ROLE { get; set; } = "User";
        }
    }
}
