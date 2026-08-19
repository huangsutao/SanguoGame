using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SanguoGame.Infrastructure.Entities;

namespace SanguoGame.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Default");

        var autoSync = configuration.GetValue("FreeSql:AutoSyncStructure", true);

        services.AddSingleton<IFreeSql>(_ =>
        {
            var orm = new FreeSqlBuilder()
                .UseConnectionString(DataType.PostgreSQL, connectionString)
                .UseAutoSyncStructure(autoSync)
                .Build();

            if (autoSync)
            {
                orm.CodeFirst.SyncStructure(
                    typeof(AccountEntity),
                    typeof(CharacterEntity),
                    typeof(CityEntity),
                    typeof(RefreshTokenEntity));
            }

            return orm;
        });

        return services;
    }
}
