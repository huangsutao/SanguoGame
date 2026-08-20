using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_daily_quest")]
[Index("uk_daily_city_day_type", "CityId,Day,Type", true)]
public sealed class DailyQuestEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "city_id", IsNullable = false)]
    public long CityId { get; set; }

    [Column(Name = "day", IsNullable = false)]
    public DateTime Day { get; set; }

    [Column(Name = "type", StringLength = 32, IsNullable = false)]
    public string Type { get; set; } = "";

    [Column(Name = "progress", IsNullable = false)]
    public int Progress { get; set; }

    [Column(Name = "claimed", IsNullable = false)]
    public bool Claimed { get; set; }
}
