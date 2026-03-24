using LMS_API.Domain.Enums;

namespace LMS_API.DTOs;

public sealed record UserDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public UserRole Role { get; init; }
}
