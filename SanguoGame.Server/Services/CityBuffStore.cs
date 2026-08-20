using System.Data.Common;
using FreeSql;
using SanguoGame.Core.Shop;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

internal static class CityBuffStore
{
    public static async Task<IReadOnlyList<ActiveBuff>> LoadAsync(
        IFreeSql orm,
        long cityId,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var query = orm.Select<BuffEntity>().Where(b => b.CityId == cityId);
        if (transaction is not null)
        {
            query = query.WithTransaction(transaction);
        }

        var rows = await query.ToListAsync(cancellationToken);
        return rows.Select(row => new ActiveBuff(row.BuffType, row.ExpireAt)).ToList();
    }
}
