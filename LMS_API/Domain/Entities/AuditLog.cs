namespace LMS_API.Domain.Entities
{
    public class AuditLog
    {
        public int Id { get; set; }

        public int? UserId { get; set; }

        public string Action { get; set; }

        public string? IpAddress { get; set; }

        public string? Device { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
