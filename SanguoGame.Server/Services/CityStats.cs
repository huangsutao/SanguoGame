using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

internal static class CityStats
{
    public static TroopCount Troops(CityEntity city) =>
        new(city.Infantry, city.Archer, city.Cavalry);

    public static void ApplyTroops(CityEntity city, TroopCount troops)
    {
        city.Infantry = troops.Infantry;
        city.Archer = troops.Archer;
        city.Cavalry = troops.Cavalry;
    }

    public static ResourceAmount Stock(CityEntity city) =>
        new(city.Grain, city.Wood, city.Iron, city.Copper);

    public static void ApplyStock(CityEntity city, ResourceAmount stock)
    {
        city.Grain = stock.Grain;
        city.Wood = stock.Wood;
        city.Iron = stock.Iron;
        city.Copper = stock.Copper;
    }

    public static TroopCount FitCap(TroopCount troops, int cap)
    {
        var infantry = troops.Infantry;
        var archer = troops.Archer;
        var cavalry = troops.Cavalry;
        var extra = infantry + archer + cavalry - cap;
        while (extra > 0 && cavalry > 0)
        {
            cavalry--;
            extra--;
        }

        while (extra > 0 && archer > 0)
        {
            archer--;
            extra--;
        }

        while (extra > 0 && infantry > 0)
        {
            infantry--;
            extra--;
        }

        return new TroopCount(infantry, archer, cavalry);
    }

    public static bool IsProtected(CityEntity city, DateTime now) =>
        city.ProtectionUntil is { } until && until > now;

    public static int BuildingLevel(IEnumerable<BuildingEntity> rows, string type) =>
        rows.FirstOrDefault(b => b.Type.Equals(type, StringComparison.OrdinalIgnoreCase))?.Level ?? 0;

    public static Dictionary<string, int> WallLevels(IEnumerable<BuildingEntity> rows) =>
        WallCatalog.All.ToDictionary(
            def => def.Type,
            def => BuildingLevel(rows, def.Type),
            StringComparer.OrdinalIgnoreCase);
}
