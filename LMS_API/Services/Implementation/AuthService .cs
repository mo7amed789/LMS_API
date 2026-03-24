using LMS_API.Common.Results;
using LMS_API.Data;
using LMS_API.Domain.Entities;
using LMS_API.Domain.Enums;
using LMS_API.DTOs;
using LMS_API.Mappings;
using LMS_API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace LMS_API.Services
{
    public class AuthService : IAuthService
    {
        private readonly LMSDbContext _context;
        private readonly IConfiguration _config;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(LMSDbContext context, IConfiguration config, IEmailService emailService, ILogger<AuthService> logger)
        {
            _context = context;
            _config = config;
            _emailService = emailService;
            _logger = logger;
        }

        // =============================
        // Register
        // =============================
        public async Task<ServiceResult<UserDto>> RegisterAsync(RegisterDto dto)
        {
            if (await _context.Users.AnyAsync(x => x.Email == dto.Email))
                return ServiceResult<UserDto>.Failure("Email already exists");

            var user = new User
            {
                Name = dto.Name,
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role = UserRole.Student,
                EmailVerificationToken = Guid.NewGuid().ToString()
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var verifyLink = $"https://localhost:5001/api/auth/verify-email?token={user.EmailVerificationToken}";

            await _emailService.SendEmailAsync(
                user.Email,
                "Verify your email",
                $"<h3>Click to verify:</h3><a href='{verifyLink}'>Verify Email</a>"
            );

            return ServiceResult<UserDto>.Success(user.ToDto(), "User registered successfully");
        }

        // =============================
        // Login
        // =============================
        public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto dto, string ip, string device)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == dto.Email);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return ServiceResult<AuthResponseDto>.Failure("Invalid email or password");

            if (!user.IsEmailVerified)
                return ServiceResult<AuthResponseDto>.Failure("Email not verified");

            _logger.LogInformation("User {Email} logged in", dto.Email);

            // Revoke old tokens
            var activeTokens = await _context.RefreshTokens
                .Where(x => x.UserId == user.Id && !x.IsRevoked)
                .ToListAsync();

            foreach (var t in activeTokens)
                t.IsRevoked = true;

            // Generate tokens
            var jwt = GenerateToken(user);
            var refreshToken = GenerateRefreshToken();

            var refreshTokenEntity = new RefreshToken
            {
                Token = BCrypt.Net.BCrypt.HashPassword(refreshToken),
                UserId = user.Id,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
                IPAddress = ip,
                Device = device
            };

            _context.RefreshTokens.Add(refreshTokenEntity);

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

        // =============================
        // Refresh Token
        // =============================
        public async Task<ServiceResult<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
        {
            var tokens = await _context.RefreshTokens
                .Include(x => x.User)
                .Where(x => !x.IsRevoked && x.ExpiresAt > DateTime.UtcNow)
                .ToListAsync();

            var storedToken = tokens.FirstOrDefault(x =>
                BCrypt.Net.BCrypt.Verify(refreshToken, x.Token));

            if (storedToken == null)
                return ServiceResult<AuthResponseDto>.Failure("Invalid refresh token");

            _logger.LogInformation("Refresh token used for user {UserId}", storedToken.UserId);

            storedToken.IsRevoked = true;

            var newJwt = GenerateToken(storedToken.User);
            var newRefreshToken = GenerateRefreshToken();

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

        // =============================
        // Verify Email
        // =============================
        public async Task<ServiceResult<bool>> VerifyEmailAsync(string token)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.EmailVerificationToken == token);

            if (user == null)
                return ServiceResult<bool>.Failure("Invalid token");

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;

            await _context.SaveChangesAsync();

            return ServiceResult<bool>.Success(true, "Email verified successfully");
        }

        // =============================
        // Logout
        // =============================
        public async Task<ServiceResult<bool>> LogoutAsync(string refreshToken)
        {
            var tokens = await _context.RefreshTokens
                .Where(x => !x.IsRevoked)
                .ToListAsync();

            var token = tokens.FirstOrDefault(x =>
                BCrypt.Net.BCrypt.Verify(refreshToken, x.Token));

            if (token == null)
                return ServiceResult<bool>.Failure("Invalid token");

            token.IsRevoked = true;

            await _context.SaveChangesAsync();

            return ServiceResult<bool>.Success(true, "Logged out successfully");
        }

        // =============================
        // Forgot Password
        // =============================
        public async Task<ServiceResult<bool>> ForgotPasswordAsync(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

            if (user == null)
                return ServiceResult<bool>.Success(true);

            var rawToken = Guid.NewGuid().ToString();
            var hashedToken = BCrypt.Net.BCrypt.HashPassword(rawToken);

            user.PasswordResetToken = hashedToken;
            user.PasswordResetTokenExpires = DateTime.UtcNow.AddHours(1);

            await _context.SaveChangesAsync();

            var link = $"https://localhost:5001/reset-password?token={rawToken}";

            await _emailService.SendEmailAsync(
                user.Email,
                "Reset Password",
                $"<h3>Reset your password:</h3><a href='{link}'>Reset</a>"
            );

            return ServiceResult<bool>.Success(true, "Reset link sent");
        }

        // =============================
        // Reset Password
        // =============================
        public async Task<ServiceResult<bool>> ResetPasswordAsync(string token, string newPassword)
        {
            var users = await _context.Users
                .Where(x => x.PasswordResetToken != null)
                .ToListAsync();

            var user = users.FirstOrDefault(x =>
                BCrypt.Net.BCrypt.Verify(token, x.PasswordResetToken));

            if (user == null || user.PasswordResetTokenExpires < DateTime.UtcNow)
                return ServiceResult<bool>.Failure("Invalid or expired token");

            _logger.LogInformation("Password reset for user {UserId}", user.Id);

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpires = null;

            await _context.SaveChangesAsync();

            return ServiceResult<bool>.Success(true, "Password reset successful");
        }

        // =============================
        // Generate JWT
        // =============================
        private string GenerateToken(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            };

            var keyValue = _config["Jwt:Key"];

            if (string.IsNullOrEmpty(keyValue))
                throw new Exception("JWT Key is missing");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyValue));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(15), // ✅ Best practice
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateRefreshToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
    }
}