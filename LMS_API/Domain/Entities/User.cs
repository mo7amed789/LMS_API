using LMS_API.Domain.Enums;
namespace LMS_API.Domain.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsEmailVerified { get; set; } = false;
        public string? EmailVerificationToken { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public string? PasswordResetToken { get; set; }
        public DateTime? PasswordResetTokenExpires { get; set; }

        public UserRole Role { get; set; } = UserRole.Student;
        public ICollection<RefreshToken> RefreshTokens { get; set; }
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
        public ICollection<Course> Courses { get; set; } = new List<Course>();
    }
}