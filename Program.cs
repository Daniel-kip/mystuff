using System.Data;
using System.Security.Cryptography;
using System.Text;
using DelTechApi.Hubs;
using DelTechApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using MySql.Data.MySqlClient;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ----------------------------
// Logging Setup
// ----------------------------
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/deltech-api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// ----------------------------
// Africa's Talking Config
// ----------------------------
var atConfig = configuration.GetSection("AfricasTalking");
var atSettings = new AfricasTalkingSettings
{
    Username = atConfig["Username"] ?? "",
    ApiKey = atConfig["ApiKey"] ?? "",
    SenderName = atConfig["SenderName"] ?? "DelTech"
};
if (string.IsNullOrEmpty(atSettings.Username) || string.IsNullOrEmpty(atSettings.ApiKey))
    Log.Warning("Africa's Talking configuration incomplete");

// ----------------------------
// Controllers + JSON
// ----------------------------
builder.Services.AddControllers()
    .AddNewtonsoftJson(opt =>
    {
        opt.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
        opt.SerializerSettings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
    });
builder.Services.AddEndpointsApiExplorer();

// ----------------------------
// Swagger + JWT
// ----------------------------
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DelTech API",
        Version = "v1",
        Description = "API for SMS and device management"
    });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ----------------------------
// Health Checks
// ----------------------------
builder.Services.AddHealthChecks()
    .AddCheck<JwtKeyRotationHealthCheck>("JWT Key Rotation");

// ----------------------------
// Dependency Injection
// ----------------------------
builder.Services.AddScoped<IDatabaseService, DatabaseService>();
builder.Services.AddScoped<IMessageLogService, MessageLogService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<SmsService>();
builder.Services.AddScoped<UserSettingsService>();
builder.Services.AddSingleton<ICacheService, DistributedCacheService>();
builder.Services.AddHostedService<BackgroundPollingService>();
builder.Services.AddHostedService<DatabaseMaintenanceService>();
builder.Services.AddDistributedMemoryCache();

// ----------------------------
// HTTP Client (Africa's Talking)
// ----------------------------
builder.Services.AddHttpClient("AfricasTalking", client =>
{
    client.BaseAddress = new Uri("https://api.africastalking.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ----------------------------
// Data Protection + Key Rotation
// ----------------------------
var keysDirectory = Path.Combine(AppContext.BaseDirectory, "keys");
Directory.CreateDirectory(keysDirectory);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("DelTechAPI")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

builder.Services.AddSingleton<JwtKeyRotationService>(sp =>
{
    var protector = sp.GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("JWT-Key-Protector");

    var activePath = Path.Combine(keysDirectory, "jwtkey_active.bin");
    var prevPath = Path.Combine(keysDirectory, "jwtkey_previous.bin");
    var archivePath = Path.Combine(keysDirectory, "jwtkey_archive.bin");

    var svc = new JwtKeyRotationService(protector, activePath, prevPath, archivePath);
    svc.InitializeOrRotateKeys();
    JwtKeyRotationServiceStatic.ActiveKeyExists = svc.HasActiveKey;
    return svc;
});

// ----------------------------
// CORS
// ----------------------------
builder.Services.AddCors(opt =>
{
    opt.AddPolicy("AllowFrontend", p =>
        p.WithOrigins(
            "http://localhost:3000",
            "https://localhost:3000",
            "http://localhost:5031")
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials());
});

// ----------------------------
// Authentication + JWT
// ----------------------------
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SaveToken = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = configuration["Jwt:Issuer"] ?? "DelTechAPI",
            ValidAudience = configuration["Jwt:Audience"] ?? "DelTechClients",

            IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
            {
                using var scope = builder.Services.BuildServiceProvider().CreateScope();
                var keyRotationService = scope.ServiceProvider.GetRequiredService<JwtKeyRotationService>();
                var keys = keyRotationService.GetValidRawKeys()
                    .Select(k => new SymmetricSecurityKey(k));
                return keys;
            },

            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/devicehub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

// ----------------------------
// Authorization Policies
// ----------------------------
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    options.AddPolicy("DeviceAccess", p => p.RequireRole("Admin", "DeviceManager"));
    options.AddPolicy("SmsAccess", p => p.RequireRole("Admin", "SmsManager"));
});

// ----------------------------
// SignalR
// ----------------------------
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024;
}).AddJsonProtocol(opt =>
{
    opt.PayloadSerializerOptions.PropertyNamingPolicy = null;
});

// ----------------------------
// Response Compression
// ----------------------------
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

// ======================================================
// Build app
// ======================================================
var app = builder.Build();

// ----------------------------
// Middleware Pipeline
// ----------------------------
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception");

        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "Internal server error",
            requestId = ctx.TraceIdentifier
        });
    }
});

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DelTech API v1");
        c.RoutePrefix = "api-docs";
    });
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseResponseCompression();
app.UseRouting();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

// ----------------------------
// Endpoints
// ----------------------------
app.MapControllers();
app.MapHub<DeviceHub>("/devicehub");
app.MapHealthChecks("/health");
app.Map("/error", () => Results.Problem("Unexpected error", statusCode: 500));

app.MapGet("/api/diagnostic/endpoints", (IEnumerable<EndpointDataSource> sources) =>
{
    var endpoints = sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>();
    return Results.Ok(new
    {
        totalEndpoints = endpoints.Count(),
        authEndpoints = endpoints
            .Where(e => e.DisplayName?.Contains("AuthController") == true)
            .Select(e => new
            {
                pattern = e.RoutePattern.RawText,
                methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods,
                displayName = e.DisplayName
            })
    });
});

app.Run();

// ----------------------------
// Supporting Models
// ----------------------------
public class AfricasTalkingSettings
{
    public string Username { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string SenderName { get; set; } = "DelTech";
}
