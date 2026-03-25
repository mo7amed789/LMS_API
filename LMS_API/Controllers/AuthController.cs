using LMS_API.DTOs;
using LMS_API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace LMS_API.Controllers;

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
            return ValidationProblem(ModelState);

        var result = await _service.RegisterAsync(dto);
        if (!result.IsSuccess)
            return Conflict(result.Message);

        return Ok(result);
    }

    [EnableRateLimiting("AuthPolicy")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var device = Request.Headers.UserAgent.ToString();

        var result = await _service.LoginAsync(dto, ip, device);
        if (!result.IsSuccess || result.Data is null)
            return Unauthorized(result.Message);

        Response.Cookies.Append("refreshToken", result.Data.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            IsEssential = true
        });

        return Ok(new
        {
            token = result.Data.Token
        });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult GetMe()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var role = User.FindFirstValue(ClaimTypes.Role);

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
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized("No refresh token");

        var result = await _service.RefreshTokenAsync(refreshToken);
        if (!result.IsSuccess || result.Data is null)
            return Unauthorized(result.Message);

        Response.Cookies.Append("refreshToken", result.Data.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            IsEssential = true
        });

        return Ok(new { token = result.Data.Token });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest("No refresh token");

        var result = await _service.LogoutAsync(refreshToken);
        if (!result.IsSuccess)
            return BadRequest(result.Message);

        Response.Cookies.Delete("refreshToken");
        return Ok("Logged out successfully");
    }

    [EnableRateLimiting("AuthPolicy")]
    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        var result = await _service.VerifyEmailAsync(token);
        if (!result.IsSuccess)
            return BadRequest(result.Message);

        return Ok(new { message = "Email verified successfully" });
    }

    [EnableRateLimiting("AuthPolicy")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        await _service.ForgotPasswordAsync(dto.Email);
        return Ok("If the email exists, a reset link has been sent.");
    }

    [EnableRateLimiting("AuthPolicy")]
    [HttpGet("reset-password")]
public IActionResult ResetPasswordPage([FromQuery] string token)
{
    return Ok(new { token });
}
}
