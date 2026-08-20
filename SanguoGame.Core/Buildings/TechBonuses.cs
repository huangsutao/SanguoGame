namespace SanguoGame.Core.Buildings;

public static class TechBonuses
{
    public const string DrillHall = "drillHall";
    public const string DefenseHall = "defenseHall";
    public const string ResourceHall = "resourceHall";
    public const int MaxRecruitDiscountPercent = 50;

    public static int AcademyAttackPercent(int academyLevel) => 2 * Math.Max(0, academyLevel);

    public static int TroopPowerPercent(int drillHallLevel) => 3 * Math.Max(0, drillHallLevel);

    public static int RecruitDiscountPercent(int drillHallLevel) =>
        Math.Min(MaxRecruitDiscountPercent, 2 * Math.Max(0, drillHallLevel));

    public static int WallDefenseFlat(int defenseHallLevel) => 2 * Math.Max(0, defenseHallLevel);

    public static double TrapBonus(int defenseHallLevel) => 0.01 * Math.Max(0, defenseHallLevel);

    public static int ProductionPercent(int resourceHallLevel) => 5 * Math.Max(0, resourceHallLevel);

    public static int BoostedRate(OuterFieldDef def, int level, int resourceHallLevel) =>
        ApplyPercent(def.RatePerHour(level), ProductionPercent(resourceHallLevel));

    public static int BoostedCap(OuterFieldDef def, int level, int resourceHallLevel) =>
        ApplyPercent(def.FieldCap(level), ProductionPercent(resourceHallLevel));

    public static int ApplyPercent(int value, int percent)
    {
        if (value <= 0)
        {
            return 0;
        }

        return (int)Math.Floor(value * (100d + Math.Max(0, percent)) / 100d);
    }

    public static ResourceAmount Discount(ResourceAmount cost, int percent)
    {
        var clipped = Math.Clamp(percent, 0, MaxRecruitDiscountPercent);
        if (clipped <= 0)
        {
            return cost;
        }

        return cost.ScaleFloor((100 - clipped) / 100d);
    }
}
