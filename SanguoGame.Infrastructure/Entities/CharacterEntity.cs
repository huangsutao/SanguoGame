using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_character")]
[Index("uk_character_account", "AccountId", true)]
[Index("uk_character_name", "Name", true)]
public sealed class CharacterEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "account_id", IsNullable = false)]
    public long AccountId { get; set; }

    [Column(Name = "name", StringLength = 12, IsNullable = false)]
    public string Name { get; set; } = "";

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
