using LMS_API.DTOs;
using LMS_API.Services;
using LMS_API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LMS_API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _service;

        public AuthController(IAuthService service)
        {
            _service = service;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _service.RegisterAsync(dto);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var device = Request.Headers["User-Agent"].ToString();
            if (!result.IsSuccess)
                return BadRequest(result.Message);

            return Ok(result);
        }
        [EnableRateLimiting("AuthPolicy")]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // 📱 Extract device + IP
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var device = Request.Headers["User-Agent"].ToString();

            var result = await _service.LoginAsync(dto, ip, device);

            if (!result.IsSuccess)
                return Unauthorized(result.Message);

            return Ok(result.Data);
        }

        [Authorize]
        [HttpGet("me")]
        public IActionResult GetMe()
        {
            var userId = User.FindFirst("id")?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var device = Request.Headers["User-Agent"].ToString();
            return Ok(new
            {
                userId,
                email,
                role
            });
        }
     
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            var refreshToken = Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized("No refresh token");

            var result = await _service.RefreshTokenAsync(refreshToken);

            if (!result.IsSuccess)
                return Unauthorized(result.Message);

            // 🔄 Update cookie
            Response.Cookies.Append("refreshToken", result.Data.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(7)
            });

            return Ok(new
            {
                token = result.Data.Token
            });
        }
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // 🍪 هات refresh token من cookie
            var refreshToken = Request.Cookies["refreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
                return BadRequest("No refresh token");

            var result = await _service.LogoutAsync(refreshToken);

            if (!result.IsSuccess)
                return BadRequest(result.Message);

            // 🧹 امسح الكوكي
            Response.Cookies.Delete("refreshToken");

            return Ok("Logged out successfully");
        }
        [EnableRateLimiting("AuthPolicy")]
        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail(string token)
        {
            var result = await _service.VerifyEmailAsync(token);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var device = Request.Headers["User-Agent"].ToString();
            if (!result.IsSuccess)
                return BadRequest(result.Message);

            return Redirect($"https://your-frontend.com/email-verified");
        }
        [EnableRateLimiting("AuthPolicy")]
        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var result = await _service.ForgotPasswordAsync(dto.Email);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var device = Request.Headers["User-Agent"].ToString();
            return Ok("If the email exists, a reset link has been sent.");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var result = await _service.ResetPasswordAsync(dto.Token, dto.NewPassword);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var device = Request.Headers["User-Agent"].ToString();
            if (!result.IsSuccess)
                return BadRequest(result.Message);

            return Ok(result.Message);
        }
    }
}