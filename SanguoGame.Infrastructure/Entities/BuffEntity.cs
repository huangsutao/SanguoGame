using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_buff")]
[Index("uk_buff_city_type", "CityId,BuffType", true)]
public sealed class BuffEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "city_id", IsNullable = false)]
    public long CityId { get; set; }

    [Column(Name = "buff_type", StringLength = 32, IsNullable = false)]
    public string BuffType { get; set; } = "";

    [Column(Name = "expire_at", IsNullable = false)]
    public DateTime ExpireAt { get; set; }
}
