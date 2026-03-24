namespace LMS_API.DTOs
{
    public record AuthResponseDto
    {
        public string Token { get; init; }
        public string RefreshToken { get; init; }
    }
}
