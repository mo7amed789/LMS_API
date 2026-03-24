namespace LMS_API.DTOs;

public sealed record AuthResponseDto
{
    public string Token { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
}
