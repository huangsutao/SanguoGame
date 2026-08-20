using FreeSql.DataAnnotations;
using SanguoGame.Core.Army;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_march")]
[Index("idx_march_from_status", "FromCityId,Status")]
public sealed class MarchEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "from_city_id", IsNullable = false)]
    public long FromCityId { get; set; }

    [Column(Name = "target_type", IsNullable = false)]
    public MarchTargetType TargetType { get; set; }

    [Column(Name = "target_id", IsNullable = false)]
    public long TargetId { get; set; }

    [Column(Name = "from_x", IsNullable = false)]
    public int FromX { get; set; }

    [Column(Name = "from_y", IsNullable = false)]
    public int FromY { get; set; }

    [Column(Name = "to_x", IsNullable = false)]
    public int ToX { get; set; }

    [Column(Name = "to_y", IsNullable = false)]
    public int ToY { get; set; }

    [Column(Name = "infantry", IsNullable = false)]
    public int Infantry { get; set; }

    [Column(Name = "archer", IsNullable = false)]
    public int Archer { get; set; }

    [Column(Name = "cavalry", IsNullable = false)]
    public int Cavalry { get; set; }

    [Column(Name = "depart_at", IsNullable = false)]
    public DateTime DepartAt { get; set; }

    [Column(Name = "arrive_at", IsNullable = false)]
    public DateTime ArriveAt { get; set; }

    [Column(Name = "status", IsNullable = false)]
    public MarchStatus Status { get; set; }

    [Column(Name = "kind", IsNullable = false)]
    public MarchKind Kind { get; set; }
}
