namespace LMS_API.DTOs
{
    public sealed record LoginDto
    {
        public string Email { get; init; }
        public string Password { get; init; }
    }
}
