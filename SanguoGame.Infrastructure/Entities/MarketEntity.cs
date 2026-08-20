using FreeSql.DataAnnotations;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_market")]
[Index("uk_market_xy", "X,Y", true)]
public sealed class MarketEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "name", StringLength = 32, IsNullable = false)]
    public string Name { get; set; } = "";

    [Column(Name = "x", IsNullable = false)]
    public int X { get; set; }

    [Column(Name = "y", IsNullable = false)]
    public int Y { get; set; }
}
