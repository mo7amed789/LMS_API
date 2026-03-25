using FluentValidation;
using LMS_API;
using LMS_API.Data;
using LMS_API.Middleware;
using LMS_API.Services;
using LMS_API.Services.Interfaces;
using LMS_API.Validators;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NSwag;
using NSwag.Generation.Processors.Security;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ================= LOGGING =================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// ================= DATABASE =================
builder.Services.AddDbContext<LMSDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.")
    ));

// ================= OPTIONS =================
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .Validate(x => !string.IsNullOrWhiteSpace(x.Key), "JWT Key is required")
    .Validate(x => !string.IsNullOrWhiteSpace(x.Issuer), "JWT Issuer is required")
    .Validate(x => !string.IsNullOrWhiteSpace(x.Audience), "JWT Audience is required")
    .ValidateOnStart();

builder.Services.AddOptions<AppUrlSettings>()
    .Bind(builder.Configuration.GetSection("AppUrls"))
    .Validate(x => !string.IsNullOrWhiteSpace(x.ApiBaseUrl), "ApiBaseUrl is required")
    .Validate(x => !string.IsNullOrWhiteSpace(x.FrontendBaseUrl), "FrontendBaseUrl is required")
    .ValidateOnStart();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("JWT settings are missing");

// ================= AUTH =================
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Key)
            ),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ================= VALIDATION =================
builder.Services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

// ================= RATE LIMIT =================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("AuthPolicy", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
});

// ================= CORS =================
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        var frontendUrl = builder.Configuration["AppUrls:FrontendBaseUrl"];

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            policy.WithOrigins(frontendUrl)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// ================= SERVICES =================
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// ================= CONTROLLERS =================
builder.Services.AddControllers()
    .AddJsonOptions(x =>
        x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// ================= EXTRA =================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

// ================= SWAGGER =================
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "LMS API";
    config.Version = "v1";

    config.AddSecurity("JWT", Enumerable.Empty<string>(), new OpenApiSecurityScheme
    {
        Type = OpenApiSecuritySchemeType.ApiKey,
        Name = "Authorization",
        In = OpenApiSecurityApiKeyLocation.Header,
        Description = "Bearer {token}"
    });

    config.OperationProcessors.Add(
        new AspNetCoreOperationSecurityScopeProcessor("JWT")
    );
});

// ================= FORWARDED HEADERS =================
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

// ================= BUILD =================
var app = builder.Build();

// ================= MIDDLEWARE =================
app.UseForwardedHeaders();

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (ctx, http) =>
    {
        ctx.Set("IP", http.Connection.RemoteIpAddress?.ToString());
        ctx.Set("UserAgent", http.Request.Headers["User-Agent"].ToString());
    };
});

app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");

// 🔐 SECURITY HEADERS
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    await next();
});

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// ================= ENDPOINTS =================
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();