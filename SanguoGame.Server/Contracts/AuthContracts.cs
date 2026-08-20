using System.ComponentModel.DataAnnotations;

namespace SanguoGame.Server.Contracts;

public sealed class RegisterRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(16)]
    [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "用户名仅允许字母、数字和下划线")]
    public string Username { get; set; } = "";

    [Required]
    [MinLength(8)]
    [MaxLength(64)]
    public string Password { get; set; } = "";
}

public sealed class LoginRequest
{
    [Required]
    [MinLength(1)]
    public string Username { get; set; } = "";

    [Required]
    [MinLength(1)]
    public string Password { get; set; } = "";
}

public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";
}

public sealed class LogoutRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";
}

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string TokenType = "Bearer");

public sealed record SessionCityDto(long Id, string Name, int X, int Y);

public sealed record SessionCharacterDto(long Id, string Name);

public sealed record SessionResponse(
    long AccountId,
    string Username,
    SessionCharacterDto? Character,
    SessionCityDto? City);
