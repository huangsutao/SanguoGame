namespace SanguoGame.Server.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SanguoGame";

    public string Audience { get; set; } = "SanguoGame.Web";

    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 120;

    public int RefreshTokenDays { get; set; } = 14;
}
