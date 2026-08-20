using FreeSql.DataAnnotations;
using SanguoGame.Core.Social;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_alliance_application")]
[Index("ix_alliance_application_character", "CharacterId,Status")]
[Index("ix_alliance_application_alliance", "AllianceId,Status")]
public sealed class AllianceApplicationEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "alliance_id", IsNullable = false)]
    public long AllianceId { get; set; }

    [Column(Name = "character_id", IsNullable = false)]
    public long CharacterId { get; set; }

    [Column(Name = "status", IsNullable = false)]
    public AllianceRequestStatus Status { get; set; }

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
