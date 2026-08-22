using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_recruit")]
[Index("idx_recruit_city", "CityId")]
public sealed class RecruitEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "city_id", IsNullable = false)]
    public long CityId { get; set; }

    [Column(Name = "troop_type", StringLength = 16, IsNullable = false)]
    public string TroopType { get; set; } = "";

    [Column(Name = "count", IsNullable = false)]
    public int Count { get; set; }

    [Column(Name = "finish_at", IsNullable = false)]
    public DateTime FinishAt { get; set; }
}
