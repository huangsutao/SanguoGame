using System.Data.Common;
using FreeSql;
using SanguoGame.Infrastructure;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

public static class MapCellKinds
{
    public const string City = "city";
    public const string Outpost = "outpost";
    public const string Market = "market";
}

public static class WorldOccupancy
{
    public const long PlacementLockId = 87342016;

    public static async Task<bool> IsOccupiedAsync(
        IFreeSql orm,
        int x,
        int y,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null,
        long exceptCityId = 0)
    {
        var cells = orm.Select<MapCellEntity>().Where(c => c.X == x && c.Y == y);
        if (exceptCityId > 0)
        {
            cells = cells.Where(c => !(c.Kind == MapCellKinds.City && c.OwnerId == exceptCityId));
        }

        if (transaction is not null)
        {
            cells = cells.WithTransaction(transaction);
        }

        if (await cells.AnyAsync(cancellationToken))
        {
            return true;
        }

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

    public static async Task<bool> TryClaimAsync(
        IFreeSql orm,
        int x,
        int y,
        string kind,
        long ownerId,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var insert = orm.Insert(new MapCellEntity
        {
            X = x,
            Y = y,
            Kind = kind,
            OwnerId = ownerId
        });
        if (transaction is not null)
        {
            insert = insert.WithTransaction(transaction);
        }

        try
        {
            await insert.ExecuteAffrowsAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return false;
        }
    }

    public static async Task ReleaseAsync(
        IFreeSql orm,
        int x,
        int y,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var delete = orm.Delete<MapCellEntity>().Where(c => c.X == x && c.Y == y);
        if (transaction is not null)
        {
            delete = delete.WithTransaction(transaction);
        }

        await delete.ExecuteAffrowsAsync(cancellationToken);
    }

    public static async Task SetOwnerAsync(
        IFreeSql orm,
        int x,
        int y,
        long ownerId,
        CancellationToken cancellationToken,
        DbTransaction transaction)
    {
        await orm.Update<MapCellEntity>()
            .WithTransaction(transaction)
            .Where(c => c.X == x && c.Y == y)
            .Set(c => c.OwnerId, ownerId)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    public static async Task<long?> TryInsertOccupiedAsync(
        IFreeSql orm,
        int x,
        int y,
        string kind,
        Func<DbTransaction, Task<long>> insertOwnerAsync,
        CancellationToken cancellationToken)
    {
        using var conn = await orm.Ado.MasterPool.GetAsync();
        await using var transaction = await conn.Value.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!await TryClaimAsync(orm, x, y, kind, 0, cancellationToken, transaction))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var ownerId = await insertOwnerAsync(transaction);
            await SetOwnerAsync(orm, x, y, ownerId, cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);
            return ownerId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
