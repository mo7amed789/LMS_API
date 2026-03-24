using System.ComponentModel.DataAnnotations;

namespace LMS_API.DTOs;

public sealed class ResetPasswordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}
