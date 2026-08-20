using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_city")]
[Index("uk_city_character", "CharacterId", true)]
[Index("uk_city_xy", "X,Y", true)]
public sealed class CityEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "character_id", IsNullable = false)]
    public long CharacterId { get; set; }

    [Column(Name = "name", StringLength = 32, IsNullable = false)]
    public string Name { get; set; } = "";

    [Column(Name = "x", IsNullable = false)]
    public int X { get; set; }

    [Column(Name = "y", IsNullable = false)]
    public int Y { get; set; }

    [Column(Name = "grain", IsNullable = false)]
    public int Grain { get; set; } = 2000;

    [Column(Name = "wood", IsNullable = false)]
    public int Wood { get; set; } = 2000;

    [Column(Name = "iron", IsNullable = false)]
    public int Iron { get; set; } = 2000;

    [Column(Name = "copper", IsNullable = false)]
    public int Copper { get; set; } = 2000;

    [Column(Name = "infantry", IsNullable = false)]
    public int Infantry { get; set; }

    [Column(Name = "archer", IsNullable = false)]
    public int Archer { get; set; }

    [Column(Name = "cavalry", IsNullable = false)]
    public int Cavalry { get; set; }

    [Column(Name = "protection_until")]
    public DateTime? ProtectionUntil { get; set; }

    [Column(Name = "yuanbao", IsNullable = false)]
    public int Yuanbao { get; set; }

    [Column(Name = "recruit_type", StringLength = 16)]
    public string? RecruitType { get; set; }

    [Column(Name = "recruit_count", IsNullable = false)]
    public int RecruitCount { get; set; }

    [Column(Name = "recruit_finish_at")]
    public DateTime? RecruitFinishAt { get; set; }

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
