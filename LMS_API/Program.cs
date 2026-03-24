using LMS_API.Data;
using LMS_API.Middleware;
using LMS_API.Services;
using LMS_API.Services.Interfaces;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NSwag;
using NSwag.Generation.Processors.Security;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);


// =============================
// Logging (Serilog)
// =============================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();


// =============================
// Database
// =============================
builder.Services.AddDbContext<LMSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));


// =============================
// Services (DI)
// =============================
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();


// =============================
// JWT Authentication
// =============================
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new Exception("JWT Key is missing in configuration");

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,

            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)
            ),

            ClockSkew = TimeSpan.Zero // 🔥 مهم
        };
    });


// =============================
// Rate Limiting
// =============================
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("AuthPolicy", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});


// =============================
// Controllers + JSON
// =============================
builder.Services.AddControllers()
    .AddJsonOptions(x =>
        x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);


// =============================
// Swagger (NSwag)
// =============================
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "LMS API";
    config.Version = "v1";
    config.Description = "Learning Management System API";

    // 🔐 JWT Security (FIXED)
    config.AddSecurity("JWT", Enumerable.Empty<string>(), new NSwag.OpenApiSecurityScheme
    {
        Type = NSwag.OpenApiSecuritySchemeType.ApiKey,
        Name = "Authorization",
        In = NSwag.OpenApiSecurityApiKeyLocation.Header,
        Description = "Enter: Bearer {your token}"
    });

    config.OperationProcessors.Add(
        new AspNetCoreOperationSecurityScopeProcessor("JWT"));
});


// =============================
// Build App
// =============================
var app = builder.Build();


// =============================
// Middleware Pipeline
// =============================
app.UseSerilogRequestLogging();

app.UseMiddleware<ExceptionMiddleware>();

// ✅ NSwag الصحيح
app.UseOpenApi();
app.UseSwaggerUi();

app.UseHttpsRedirection();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();