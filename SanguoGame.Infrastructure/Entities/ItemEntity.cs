using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_item")]
[Index("uk_item_city_type", "CityId,ItemType", true)]
public sealed class ItemEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "city_id", IsNullable = false)]
    public long CityId { get; set; }

    [Column(Name = "item_type", StringLength = 32, IsNullable = false)]
    public string ItemType { get; set; } = "";

    [Column(Name = "count", IsNullable = false)]
    public int Count { get; set; }
}
