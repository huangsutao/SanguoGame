using System.ComponentModel.DataAnnotations;
using SanguoGame.Core.Social;

namespace SanguoGame.Server.Contracts;

public sealed record MailDto(
    long Id,
    MailType Type,
    string Title,
    string Body,
    string? RelatedType,
    long? RelatedId,
    bool IsRead,
    DateTime CreatedAt);

public sealed record MailListDto(
    int UnreadCount,
    IReadOnlyList<MailDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed class CreateAllianceRequest
{
    [Required]
    [MinLength(AllianceRules.NameMinLength)]
    [MaxLength(AllianceRules.NameMaxLength)]
    public string Name { get; set; } = "";
}

public sealed class UpdateAllianceNoticeRequest
{
    [MaxLength(AllianceRules.NoticeMaxLength)]
    public string Notice { get; set; } = "";
}

public sealed class InviteAllianceRequest
{
    [Required]
    [MinLength(2)]
    [MaxLength(12)]
    public string CharacterName { get; set; } = "";
}

public sealed class KickAllianceRequest
{
    [Range(1, long.MaxValue)]
    public long CharacterId { get; set; }
}

public sealed record AllianceMemberDto(
    long CharacterId,
    string Name,
    AllianceRole Role,
    DateTime JoinedAt);

public sealed record AllianceSummaryDto(
    long Id,
    string Name,
    int MemberCount,
    string LeaderName);

public sealed record AllianceDetailDto(
    long Id,
    string Name,
    string Notice,
    long LeaderCharacterId,
    int MemberCount,
    AllianceRole? MyRole,
    IReadOnlyList<AllianceMemberDto> Members);

public sealed record AllianceInviteDto(
    long Id,
    long AllianceId,
    string AllianceName,
    string InviterName,
    DateTime CreatedAt);

public sealed record AllianceApplicationDto(
    long Id,
    long AllianceId,
    long CharacterId,
    string CharacterName,
    DateTime CreatedAt);

public sealed record AlliancePendingDto(
    IReadOnlyList<AllianceInviteDto> Invites,
    IReadOnlyList<AllianceApplicationDto> Applications);

public sealed record RankingEntryDto(
    int Rank,
    long CityId,
    string CharacterName,
    string CityName,
    int Score,
    bool IsAi,
    string? AllianceName);

public sealed record RankingDto(
    RankingType Type,
    DateTime ServerTime,
    int? MyRank,
    int MyScore,
    IReadOnlyList<RankingEntryDto> Items);
