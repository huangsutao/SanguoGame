using System.Data.Common;
using FreeSql;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

internal static class WorldOccupancy
{
    public static async Task<bool> IsOccupiedAsync(
        IFreeSql orm,
        int x,
        int y,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null,
        long exceptCityId = 0)
    {
        var cities = orm.Select<CityEntity>().Where(c => c.X == x && c.Y == y);
        if (exceptCityId > 0)
        {
            cities = cities.Where(c => c.Id != exceptCityId);
        }

        if (transaction is not null)
        {
            cities = cities.WithTransaction(transaction);
        }

        if (await cities.AnyAsync(cancellationToken))
        {
            return true;
        }

        var outposts = orm.Select<OutpostEntity>().Where(o => o.X == x && o.Y == y);
        var markets = orm.Select<MarketEntity>().Where(m => m.X == x && m.Y == y);
        if (transaction is not null)
        {
            outposts = outposts.WithTransaction(transaction);
            markets = markets.WithTransaction(transaction);
        }

        if (await outposts.AnyAsync(cancellationToken))
        {
            return true;
        }

        return await markets.AnyAsync(cancellationToken);
    }
}
