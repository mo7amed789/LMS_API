using System.ComponentModel.DataAnnotations;

public sealed record RegisterDto
{
    [Required]
    public string Name { get; init; }

    [Required, EmailAddress]
    public string Email { get; init; }

    [Required, MinLength(6)]
    public string Password { get; init; }
}