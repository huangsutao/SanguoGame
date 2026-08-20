using SanguoGame.Core.Shop;

namespace SanguoGame.Core.Army;

public static class RecruitTiming
{
    public static int SecondsPerUnit(string troopType) => troopType.ToLowerInvariant() switch
    {
        "infantry" => 2,
        "archer" => 3,
        "cavalry" => 4,
        _ => 2
    };

    public static int DurationSeconds(string troopType, int count, int speedPercent)
    {
        if (count <= 0)
        {
            return 0;
        }

        return ItemCatalog.ApplySpeed(SecondsPerUnit(troopType) * count, speedPercent);
    }
}
