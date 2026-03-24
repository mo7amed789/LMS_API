using System.ComponentModel.DataAnnotations;

namespace LMS_API.DTOs;

public sealed record LoginDto
{
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(6)]
    public string Password { get; init; } = string.Empty;
}
