using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_map_cell")]
public sealed class MapCellEntity
{
    [Column(Name = "x", IsPrimary = true, IsNullable = false)]
    public int X { get; set; }

    [Column(Name = "y", IsPrimary = true, IsNullable = false)]
    public int Y { get; set; }

    [Column(Name = "kind", StringLength = 16, IsNullable = false)]
    public string Kind { get; set; } = "";

    [Column(Name = "owner_id", IsNullable = false)]
    public long OwnerId { get; set; }
}
