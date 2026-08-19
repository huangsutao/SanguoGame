using FreeSql.DataAnnotations;
using SanguoGame.Core.Buildings;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_building")]
[Index("uk_building_city_type", "CityId,Type", true)]
public sealed class BuildingEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "city_id", IsNullable = false)]
    public long CityId { get; set; }

    [Column(Name = "type", StringLength = 32, IsNullable = false)]
    public string Type { get; set; } = "";

    [Column(Name = "level", IsNullable = false)]
    public int Level { get; set; }

    [Column(Name = "status", IsNullable = false)]
    public BuildingStatus Status { get; set; }

    [Column(Name = "target_level")]
    public int? TargetLevel { get; set; }

    [Column(Name = "finish_at")]
    public DateTime? FinishAt { get; set; }

    [Column(Name = "last_collected_at")]
    public DateTime? LastCollectedAt { get; set; }

    [Column(Name = "updated_at", IsNullable = false)]
    public DateTime UpdatedAt { get; set; }
}
