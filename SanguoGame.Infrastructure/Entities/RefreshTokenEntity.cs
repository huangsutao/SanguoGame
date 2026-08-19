using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_refresh_token")]
[Index("uk_refresh_token_hash", "TokenHash", true)]
[Index("idx_refresh_token_account", "AccountId")]
public sealed class RefreshTokenEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "account_id", IsNullable = false)]
    public long AccountId { get; set; }

    [Column(Name = "token_hash", StringLength = 64, IsNullable = false)]
    public string TokenHash { get; set; } = "";

    [Column(Name = "expires_at", IsNullable = false)]
    public DateTime ExpiresAt { get; set; }

    [Column(Name = "revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
