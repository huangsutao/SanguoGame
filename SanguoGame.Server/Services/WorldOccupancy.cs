using FreeSql;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

internal static class WorldOccupancy
{
    public static async Task<bool> IsOccupiedAsync(IFreeSql orm, int x, int y, CancellationToken cancellationToken)
    {
        if (await orm.Select<CityEntity>().AnyAsync(c => c.X == x && c.Y == y, cancellationToken))
        {
            return true;
        }

        return await orm.Select<OutpostEntity>().AnyAsync(o => o.X == x && o.Y == y, cancellationToken);
    }
}
