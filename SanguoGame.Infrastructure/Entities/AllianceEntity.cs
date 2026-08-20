using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_alliance")]
[Index("uk_alliance_name", "NameNormalized", true)]
public sealed class AllianceEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "name", StringLength = 12, IsNullable = false)]
    public string Name { get; set; } = "";

    [Column(Name = "name_normalized", StringLength = 12, IsNullable = false)]
    public string NameNormalized { get; set; } = "";

    [Column(Name = "leader_character_id", IsNullable = false)]
    public long LeaderCharacterId { get; set; }

    [Column(Name = "notice", StringLength = 200, IsNullable = false)]
    public string Notice { get; set; } = "";

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
