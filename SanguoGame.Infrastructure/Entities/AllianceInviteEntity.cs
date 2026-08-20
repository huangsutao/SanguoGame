using FreeSql.DataAnnotations;
using SanguoGame.Core.Social;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_alliance_invite")]
[Index("ix_alliance_invite_target", "TargetCharacterId,Status")]
[Index("ix_alliance_invite_alliance", "AllianceId,Status")]
public sealed class AllianceInviteEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "alliance_id", IsNullable = false)]
    public long AllianceId { get; set; }

    [Column(Name = "inviter_character_id", IsNullable = false)]
    public long InviterCharacterId { get; set; }

    [Column(Name = "target_character_id", IsNullable = false)]
    public long TargetCharacterId { get; set; }

    [Column(Name = "status", IsNullable = false)]
    public AllianceRequestStatus Status { get; set; }

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
