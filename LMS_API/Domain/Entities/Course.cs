namespace LMS_API.Domain.Entities
{
    public class Course
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int InstructorId { get; set; }

        public User Instructor { get; set; }

        public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }
}