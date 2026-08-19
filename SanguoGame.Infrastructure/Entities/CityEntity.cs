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

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
