using FreeSql.DataAnnotations;
using SanguoGame.Core.Army;

namespace SanguoGame.Infrastructure.Entities;

[Table(Name = "sg_battle_report")]
[Index("uk_report_march", "MarchId", true)]
public sealed class BattleReportEntity
{
    [Column(Name = "id", IsIdentity = true, IsPrimary = true)]
    public long Id { get; set; }

    [Column(Name = "march_id", IsNullable = false)]
    public long MarchId { get; set; }

    [Column(Name = "attacker_city_id", IsNullable = false)]
    public long AttackerCityId { get; set; }

    [Column(Name = "defender_type", IsNullable = false)]
    public MarchTargetType DefenderType { get; set; }

    [Column(Name = "defender_id", IsNullable = false)]
    public long DefenderId { get; set; }

    [Column(Name = "attacker_won", IsNullable = false)]
    public bool AttackerWon { get; set; }

    [Column(Name = "atk_inf_before", IsNullable = false)]
    public int AtkInfBefore { get; set; }

    [Column(Name = "atk_arc_before", IsNullable = false)]
    public int AtkArcBefore { get; set; }

    [Column(Name = "atk_cav_before", IsNullable = false)]
    public int AtkCavBefore { get; set; }

    [Column(Name = "atk_inf_after", IsNullable = false)]
    public int AtkInfAfter { get; set; }

    [Column(Name = "atk_arc_after", IsNullable = false)]
    public int AtkArcAfter { get; set; }

    [Column(Name = "atk_cav_after", IsNullable = false)]
    public int AtkCavAfter { get; set; }

    [Column(Name = "def_inf_before", IsNullable = false)]
    public int DefInfBefore { get; set; }

    [Column(Name = "def_arc_before", IsNullable = false)]
    public int DefArcBefore { get; set; }

    [Column(Name = "def_cav_before", IsNullable = false)]
    public int DefCavBefore { get; set; }

    [Column(Name = "def_inf_after", IsNullable = false)]
    public int DefInfAfter { get; set; }

    [Column(Name = "def_arc_after", IsNullable = false)]
    public int DefArcAfter { get; set; }

    [Column(Name = "def_cav_after", IsNullable = false)]
    public int DefCavAfter { get; set; }

    [Column(Name = "loot_grain", IsNullable = false)]
    public int LootGrain { get; set; }

    [Column(Name = "loot_wood", IsNullable = false)]
    public int LootWood { get; set; }

    [Column(Name = "loot_iron", IsNullable = false)]
    public int LootIron { get; set; }

    [Column(Name = "loot_copper", IsNullable = false)]
    public int LootCopper { get; set; }

    [Column(Name = "seed", IsNullable = false)]
    public int Seed { get; set; }

    [Column(Name = "summary", StringLength = 200, IsNullable = false)]
    public string Summary { get; set; } = "";

    [Column(Name = "created_at", IsNullable = false)]
    public DateTime CreatedAt { get; set; }
}
