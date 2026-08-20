using FreeSql.DataAnnotations;
using SanguoGame.Core.Social;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_alliance_member")]
[Index("uk_alliance_member_character", "CharacterId", true)]
[Index("ix_alliance_member_alliance", "AllianceId")]
public sealed class AllianceMemberEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "alliance_id", IsNullable = false)]
    public long AllianceId { get; set; }

    [Column(Name = "character_id", IsNullable = false)]
    public long CharacterId { get; set; }

    [Column(Name = "role", IsNullable = false)]
    public AllianceRole Role { get; set; }

    [Column(Name = "joined_at", IsNullable = false)]
    public DateTime JoinedAt { get; set; }
}
