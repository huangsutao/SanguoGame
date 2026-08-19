using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_account")]
[Index("uk_account_username", "UsernameNormalized", true)]
public sealed class AccountEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "username", StringLength = 16, IsNullable = false)]
    public string Username { get; set; } = "";

    [Column(Name = "username_normalized", StringLength = 16, IsNullable = false)]
    public string UsernameNormalized { get; set; } = "";

    [Column(Name = "password_hash", StringLength = 256, IsNullable = false)]
    public string PasswordHash { get; set; } = "";

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
