namespace LMS_API.Domain.Entities
{
    public class Lesson
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string VideoUrl { get; set; } = string.Empty;

        public int CourseId { get; set; }

        public Course Course { get; set; }
    }
}