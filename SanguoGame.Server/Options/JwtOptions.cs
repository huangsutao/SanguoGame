namespace SanguoGame.Server.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SanguoGame";

    public string Audience { get; set; } = "SanguoGame.Web";

    public const string DevelopmentSigningKey = "dev-only-change-me-use-a-32-byte-secret-key!";

    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 120;

    public int RefreshTokenDays { get; set; } = 14;
}
