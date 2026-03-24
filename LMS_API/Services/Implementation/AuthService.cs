using LMS_API.Common.Results;
using LMS_API.Data;
using LMS_API.Domain.Entities;
using LMS_API.Domain.Enums;
using LMS_API.DTOs;
using LMS_API.Mappings;
using LMS_API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace LMS_API.Services;

public class AuthService : IAuthService
{
    private readonly LMSDbContext _context;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly ILogger<AuthService> _logger;
    private readonly AppUrlSettings _appUrlSettings;

    public AuthService(
        LMSDbContext context,
        IConfiguration config,
        IEmailService emailService,
        ILogger<AuthService> logger,
        IOptions<AppUrlSettings> appUrlSettings)
    {
        _context = context;
        _config = config;
        _emailService = emailService;
        _logger = logger;
        _appUrlSettings = appUrlSettings.Value;
    }

    public async Task<ServiceResult<UserDto>> RegisterAsync(RegisterDto dto)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        if (await _context.Users.AnyAsync(x => x.Email == normalizedEmail))
            return ServiceResult<UserDto>.Failure("Email already exists");

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = UserRole.Student,
            EmailVerificationToken = GenerateSecureToken()
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var verifyLink = $"{_appUrlSettings.ApiBaseUrl.TrimEnd('/')}/api/auth/verify-email?token={Uri.EscapeDataString(user.EmailVerificationToken)}";

        await _emailService.SendEmailAsync(
            user.Email,
            "Verify your email",
            $"<h3>Click to verify your email:</h3><a href='{verifyLink}'>Verify Email</a>");

        return ServiceResult<UserDto>.Success(user.ToDto(), "User registered successfully");
    }

    public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto dto, string? ip, string? device)
    {
        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return ServiceResult<AuthResponseDto>.Failure("Invalid email or password");

        if (!user.IsEmailVerified)
            return ServiceResult<AuthResponseDto>.Failure("Email not verified");

        _logger.LogInformation("User {Email} logged in", dto.Email);

        var activeTokens = await _context.RefreshTokens
            .Where(x => x.UserId == user.Id && !x.IsRevoked)
            .ToListAsync();

        foreach (var t in activeTokens)
        {
            t.IsRevoked = true;
        }

        var jwt = GenerateToken(user);
        var refreshToken = GenerateSecureToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = BCrypt.Net.BCrypt.HashPassword(refreshToken),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IPAddress = ip,
            Device = device
        });

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = user.Id,
            Action = "Login",
            IpAddress = ip,
            Device = device,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        return ServiceResult<AuthResponseDto>.Success(new AuthResponseDto
        {
            Token = jwt,
            RefreshToken = refreshToken
        }, "Login successful");
    }

    public async Task<ServiceResult<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
    {
        var tokens = await _context.RefreshTokens
            .Include(x => x.User)
            .Where(x => !x.IsRevoked && x.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        var storedToken = tokens.FirstOrDefault(x => BCrypt.Net.BCrypt.Verify(refreshToken, x.Token));

        if (storedToken == null)
            return ServiceResult<AuthResponseDto>.Failure("Invalid refresh token");

        storedToken.IsRevoked = true;

        var newJwt = GenerateToken(storedToken.User);
        var newRefreshToken = GenerateSecureToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            Token = BCrypt.Net.BCrypt.HashPassword(newRefreshToken),
            UserId = storedToken.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IPAddress = storedToken.IPAddress,
            Device = storedToken.Device
        });

        await _context.SaveChangesAsync();

        return ServiceResult<AuthResponseDto>.Success(new AuthResponseDto
        {
            Token = newJwt,
            RefreshToken = newRefreshToken
        });
    }

    public async Task<ServiceResult<bool>> VerifyEmailAsync(string token)
    {
        var user = await _context.Users.FirstOrDefaultAsync(x => x.EmailVerificationToken == token);

        if (user == null)
            return ServiceResult<bool>.Failure("Invalid token");

        user.IsEmailVerified = true;
        user.EmailVerificationToken = null;

        await _context.SaveChangesAsync();

        return ServiceResult<bool>.Success(true, "Email verified successfully");
    }

    public async Task<ServiceResult<bool>> LogoutAsync(string refreshToken)
    {
        var tokens = await _context.RefreshTokens
            .Where(x => !x.IsRevoked)
            .ToListAsync();

        var token = tokens.FirstOrDefault(x => BCrypt.Net.BCrypt.Verify(refreshToken, x.Token));

        if (token == null)
            return ServiceResult<bool>.Failure("Invalid token");

        token.IsRevoked = true;
        await _context.SaveChangesAsync();

        return ServiceResult<bool>.Success(true, "Logged out successfully");
    }

    public async Task<ServiceResult<bool>> ForgotPasswordAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail);

        if (user == null)
            return ServiceResult<bool>.Success(true);

        var rawToken = GenerateSecureToken();
        var hashedToken = BCrypt.Net.BCrypt.HashPassword(rawToken);

        user.PasswordResetToken = hashedToken;
        user.PasswordResetTokenExpires = DateTime.UtcNow.AddHours(1);

        await _context.SaveChangesAsync();

        var link = $"{_appUrlSettings.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(rawToken)}";

        await _emailService.SendEmailAsync(
            user.Email,
            "Reset Password",
            $"<h3>Reset your password:</h3><a href='{link}'>Reset Password</a>");

        return ServiceResult<bool>.Success(true, "Reset link sent");
    }

    public async Task<ServiceResult<bool>> ResetPasswordAsync(string token, string newPassword)
    {
        var users = await _context.Users
            .Where(x => x.PasswordResetToken != null)
            .ToListAsync();

        var user = users.FirstOrDefault(x => x.PasswordResetToken is not null && BCrypt.Net.BCrypt.Verify(token, x.PasswordResetToken));

        if (user == null || user.PasswordResetTokenExpires < DateTime.UtcNow)
            return ServiceResult<bool>.Failure("Invalid or expired token");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpires = null;

        await _context.SaveChangesAsync();

        return ServiceResult<bool>.Success(true, "Password reset successful");
    }

    private string GenerateToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var keyValue = _config["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(keyValue))
            throw new InvalidOperationException("JWT Key is missing");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyValue));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSecureToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}
