using System.ComponentModel.DataAnnotations;

namespace LMS_API.DTOs;

public sealed class ForgotPasswordDto
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}
