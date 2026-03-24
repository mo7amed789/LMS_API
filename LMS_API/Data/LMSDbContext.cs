using LMS_API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMS_API.Data
{
    public class LMSDbContext : DbContext
    {
        public LMSDbContext(DbContextOptions<LMSDbContext> options)
            : base(options)
        {
        }

        // DbSets
        public DbSet<User> Users { get; set; }
        public DbSet<Course> Courses { get; set; }
        public DbSet<Lesson> Lessons { get; set; }
        public DbSet<Enrollment> Enrollments { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(u => u.Id);

                entity.Property(u => u.Name)
                      .IsRequired()
                      .HasMaxLength(100);

                entity.Property(u => u.Email)
                      .IsRequired()
                      .HasMaxLength(150);

                entity.HasIndex(u => u.Email)
                      .IsUnique();

                entity.Property(u => u.PasswordHash)
                      .IsRequired();

                entity.Property(u => u.Role)
                      .IsRequired()
                      .HasMaxLength(20);
            });

            
            modelBuilder.Entity<Course>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Title)
                      .IsRequired()
                      .HasMaxLength(200);

                entity.Property(c => c.Description)
                      .HasMaxLength(1000);

                entity.HasOne(c => c.Instructor)
                      .WithMany(u => u.Courses)
                      .HasForeignKey(c => c.InstructorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            
            modelBuilder.Entity<Lesson>(entity =>
            {
                entity.HasKey(l => l.Id);

                entity.Property(l => l.Title)
                      .IsRequired()
                      .HasMaxLength(200);

                entity.Property(l => l.VideoUrl)
                      .IsRequired();

                entity.HasOne(l => l.Course)
                      .WithMany(c => c.Lessons)
                      .HasForeignKey(l => l.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            
            modelBuilder.Entity<Enrollment>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasIndex(e => new { e.UserId, e.CourseId })
                      .IsUnique(); 

                entity.HasOne(e => e.User)
                      .WithMany(u => u.Enrollments)
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Course)
                      .WithMany(c => c.Enrollments)
                      .HasForeignKey(e => e.CourseId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}