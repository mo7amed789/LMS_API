using System.ComponentModel.DataAnnotations;

namespace LMS_API.DTOs;

public sealed record RegisterDto
{
    [Required, MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}
