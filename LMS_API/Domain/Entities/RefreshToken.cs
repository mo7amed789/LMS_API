namespace LMS_API.Domain.Entities
{
    public class RefreshToken
    {
        public int Id { get; set; }

        public string Token { get; set; }

        public DateTime ExpiresAt { get; set; }
        public string? Device { get; set; }
        public string? IPAddress { get; set; }
        public bool IsRevoked { get; set; } = false;

        public int UserId { get; set; }
        public User User { get; set; }
    }
}