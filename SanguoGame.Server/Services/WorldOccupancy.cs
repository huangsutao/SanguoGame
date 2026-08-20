using FreeSql;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

internal static class WorldOccupancy
{
    public static bool IsOccupied(IFreeSql orm, int x, int y) =>
        orm.Select<CityEntity>().Any(c => c.X == x && c.Y == y)
        || orm.Select<OutpostEntity>().Any(o => o.X == x && o.Y == y);
}
