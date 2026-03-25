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

    // ========================= REGISTER =========================
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

        var verifyLink = $"{_appUrlSettings.ApiBaseUrl}/api/auth/verify-email?token={Uri.EscapeDataString(user.EmailVerificationToken)}";

        await _emailService.SendEmailAsync(
            user.Email,
            "Verify your email",
            $"<a href='{verifyLink}'>Verify Email</a>");

        return ServiceResult<UserDto>.Success(user.ToDto());
    }

    // ========================= LOGIN =========================
    public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto dto, string? ip, string? device)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return ServiceResult<AuthResponseDto>.Failure("Invalid credentials");

        if (!user.IsEmailVerified)
            return ServiceResult<AuthResponseDto>.Failure("Email not verified");

        _logger.LogInformation("User {Email} logged in", user.Email);

        // revoke old tokens
        var activeTokens = await _context.RefreshTokens
            .Where(x => x.UserId == user.Id && !x.IsRevoked)
            .ToListAsync();

        foreach (var t in activeTokens)
            t.IsRevoked = true;

        var accessToken = GenerateToken(user);
        var refreshToken = GenerateRefreshToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = HashToken(refreshToken),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
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
            Token = accessToken,
            RefreshToken = refreshToken
        });
    }

    // ========================= REFRESH =========================
    public async Task<ServiceResult<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
    {
        var hashed = HashToken(refreshToken);

        var storedToken = await _context.RefreshTokens
            .Include(x => x.User)
            .FirstOrDefaultAsync(x =>
                x.TokenHash == hashed &&
                !x.IsRevoked &&
                x.ExpiresAt > DateTime.UtcNow);

        if (storedToken == null)
            return ServiceResult<AuthResponseDto>.Failure("Invalid refresh token");

        // revoke old
        storedToken.IsRevoked = true;

        // create new
        var newRefresh = GenerateRefreshToken();

        _context.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = HashToken(newRefresh),
            UserId = storedToken.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        var newAccess = GenerateToken(storedToken.User);

        await _context.SaveChangesAsync();

        return ServiceResult<AuthResponseDto>.Success(new AuthResponseDto
        {
            Token = newAccess,
            RefreshToken = newRefresh
        });
    }

    // ========================= LOGOUT =========================
    public async Task<ServiceResult<bool>> LogoutAsync(string refreshToken)
    {
        var hashed = HashToken(refreshToken);

        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(x =>
                x.TokenHash == hashed &&
                !x.IsRevoked);

        if (token == null)
            return ServiceResult<bool>.Failure("Invalid token");

        token.IsRevoked = true;
        await _context.SaveChangesAsync();

        return ServiceResult<bool>.Success(true);
    }

    // ========================= VERIFY EMAIL =========================
    public async Task<ServiceResult<bool>> VerifyEmailAsync(string token)
    {
        var user = await _context.Users.FirstOrDefaultAsync(x => x.EmailVerificationToken == token);

        if (user == null)
            return ServiceResult<bool>.Failure("Invalid token");

        user.IsEmailVerified = true;
        user.EmailVerificationToken = null;

        await _context.SaveChangesAsync();

        return ServiceResult<bool>.Success(true);
    }

    // ========================= HELPERS =========================

    private string GenerateToken(User user)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Key"]!)
        );

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public async Task<ServiceResult<bool>> ForgotPasswordAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Email == normalizedEmail);

        // مهم: ما تكشفش هل الإيميل موجود ولا لا
        if (user == null)
            return ServiceResult<bool>.Success(true);

        var rawToken = GenerateSecureToken();
        var hashedToken = HashToken(rawToken);

        user.PasswordResetToken = hashedToken;
        user.PasswordResetTokenExpires = DateTime.UtcNow.AddHours(1);

        await _context.SaveChangesAsync();

        var link = $"{_appUrlSettings.ApiBaseUrl}/api/auth/reset-password?token={Uri.EscapeDataString(rawToken)}";

        await _emailService.SendEmailAsync(
            user.Email,
            "Reset Password",
            $"<h3>Reset your password:</h3><a href='{link}'>Reset Password</a>");

        _logger.LogInformation("Password reset requested for {Email}", user.Email);

        return ServiceResult<bool>.Success(true, "Reset link sent");
    }
    public async Task<ServiceResult<bool>> ResetPasswordAsync(string token, string newPassword)
    {
        var hashed = HashToken(token);

        var user = await _context.Users
            .FirstOrDefaultAsync(x =>
                x.PasswordResetToken == hashed &&
                x.PasswordResetTokenExpires > DateTime.UtcNow);

        if (user == null)
            return ServiceResult<bool>.Failure("Invalid or expired token");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpires = null;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Password reset successful for {Email}", user.Email);

        return ServiceResult<bool>.Success(true, "Password reset successful");
    }

    private string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static string GenerateSecureToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }
}