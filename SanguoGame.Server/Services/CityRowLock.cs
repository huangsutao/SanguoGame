using System.Data.Common;
using FreeSql;
using SanguoGame.Core;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Server.Services;

internal static class CityRowLock
{
    public static async Task<T> RunAsync<T>(
        IFreeSql orm,
        long cityId,
        Func<DbTransaction, CityEntity, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var conn = await orm.Ado.MasterPool.GetAsync();
        await using var transaction = await conn.Value.BeginTransactionAsync(cancellationToken);
        try
        {
            var city = await LockCityAsync(orm, transaction, cityId, cancellationToken);
            var result = await action(transaction, city, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static async Task<T> RunTwoAsync<T>(
        IFreeSql orm,
        long cityIdA,
        long cityIdB,
        Func<DbTransaction, CityEntity, CityEntity, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (cityIdA == cityIdB)
        {
            return await RunAsync(orm, cityIdA, (transaction, city, ct) => action(transaction, city, city, ct), cancellationToken);
        }

        var firstId = Math.Min(cityIdA, cityIdB);
        var secondId = Math.Max(cityIdA, cityIdB);
        using var conn = await orm.Ado.MasterPool.GetAsync();
        await using var transaction = await conn.Value.BeginTransactionAsync(cancellationToken);
        try
        {
            var first = await LockCityAsync(orm, transaction, firstId, cancellationToken);
            var second = await LockCityAsync(orm, transaction, secondId, cancellationToken);
            var cityA = cityIdA == firstId ? first : second;
            var cityB = cityIdB == firstId ? first : second;
            var result = await action(transaction, cityA, cityB, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static async Task<CityEntity> LockCityAsync(
        IFreeSql orm,
        DbTransaction transaction,
        long cityId,
        CancellationToken cancellationToken)
    {
        var city = await orm.Select<CityEntity>()
            .WithTransaction(transaction)
            .ForUpdate()
            .Where(c => c.Id == cityId)
            .FirstAsync(cancellationToken);
        if (city is null)
        {
            throw new BizException(ErrorCodes.NotFound, "目标城不存在");
        }

        return city;
    }
}
