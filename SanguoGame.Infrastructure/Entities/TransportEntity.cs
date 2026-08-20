using FreeSql.DataAnnotations;
using SanguoGame.Core.Market;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_transport")]
[Index("idx_transport_from_status", "FromCityId,Status")]
[Index("idx_transport_to_status", "ToCityId,Status")]
public sealed class TransportEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "kind", IsNullable = false)]
    public TransportKind Kind { get; set; }

    [Column(Name = "from_city_id", IsNullable = false)]
    public long FromCityId { get; set; }

    [Column(Name = "to_city_id", IsNullable = false)]
    public long ToCityId { get; set; }

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

    [Column(Name = "pay_grain", IsNullable = false)]
    public int PayGrain { get; set; }

    [Column(Name = "pay_wood", IsNullable = false)]
    public int PayWood { get; set; }

    [Column(Name = "pay_iron", IsNullable = false)]
    public int PayIron { get; set; }

    [Column(Name = "pay_copper", IsNullable = false)]
    public int PayCopper { get; set; }

    [Column(Name = "credit_grain", IsNullable = false)]
    public int CreditGrain { get; set; }

    [Column(Name = "credit_wood", IsNullable = false)]
    public int CreditWood { get; set; }

    [Column(Name = "credit_iron", IsNullable = false)]
    public int CreditIron { get; set; }

    [Column(Name = "credit_copper", IsNullable = false)]
    public int CreditCopper { get; set; }

    [Column(Name = "depart_at", IsNullable = false)]
    public DateTime DepartAt { get; set; }

    [Column(Name = "arrive_at", IsNullable = false)]
    public DateTime ArriveAt { get; set; }

    [Column(Name = "status", IsNullable = false)]
    public TransportStatus Status { get; set; }
}
