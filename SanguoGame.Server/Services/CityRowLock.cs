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
            var city = await orm.Select<CityEntity>()
                .WithTransaction(transaction)
                .ForUpdate()
                .Where(c => c.Id == cityId)
                .FirstAsync(cancellationToken);
            if (city is null)
            {
                throw new BizException(ErrorCodes.NotFound, "尚未建立主城");
            }

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
}
