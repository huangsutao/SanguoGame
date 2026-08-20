using System.ComponentModel.DataAnnotations;

namespace SanguoGame.Server.Contracts;

public sealed record DailyMissionDto(
    string Type,
    string Name,
    string Detail,
    int Progress,
    int Required,
    bool Claimed,
    ResourceDto Reward);

public sealed record DailyOverviewDto(
    DateTime ServerTime,
    DateTime Day,
    ResourceDto Resources,
    int ResourceCap,
    IReadOnlyList<DailyMissionDto> Missions);

public sealed class ClaimDailyRequest
{
    [Required]
    public string MissionType { get; set; } = "";
}

public sealed class ScoutRequest
{
    [Required]
    public string TargetType { get; set; } = "";

    [Range(1, long.MaxValue)]
    public long TargetId { get; set; }
}
