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
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Default");

        var autoSync = configuration.GetValue("FreeSql:AutoSyncStructure", false);

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
                    typeof(RefreshTokenEntity),
                    typeof(BuildingEntity),
                    typeof(OutpostEntity),
                    typeof(MarketEntity),
                    typeof(MarchEntity),
                    typeof(TransportEntity),
                    typeof(BattleReportEntity),
                    typeof(MailEntity),
                    typeof(AllianceEntity),
                    typeof(AllianceMemberEntity),
                    typeof(AllianceInviteEntity),
                    typeof(AllianceApplicationEntity),
                    typeof(DailyQuestEntity));

                orm.Ado.ExecuteNonQuery("""
                    CREATE UNIQUE INDEX IF NOT EXISTS uk_building_city_queue
                    ON sg_building (city_id)
                    WHERE status = 1
                    """);

                orm.Ado.ExecuteNonQuery("""
                    UPDATE sg_city
                    SET grain = 2000, wood = 2000, iron = 2000, copper = 2000
                    WHERE grain = 0 AND wood = 0 AND iron = 0 AND copper = 0
                      AND NOT EXISTS (SELECT 1 FROM sg_building b WHERE b.city_id = sg_city.id)
                    """);
            }

            return orm;
        });

        return services;
    }
}
