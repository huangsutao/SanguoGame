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
                    typeof(DailyQuestEntity),
                    typeof(ItemEntity),
                    typeof(BuffEntity),
                    typeof(RecruitEntity));

                orm.Ado.ExecuteNonQuery("""
                    UPDATE sg_city
                    SET grain = 2000, wood = 2000, iron = 2000, copper = 2000
                    WHERE grain = 0 AND wood = 0 AND iron = 0 AND copper = 0
                      AND NOT EXISTS (SELECT 1 FROM sg_building b WHERE b.city_id = sg_city.id)
                    """);
            }

            orm.Ado.ExecuteNonQuery("""
                DROP INDEX IF EXISTS uk_building_city_queue
                """);

            orm.Ado.ExecuteNonQuery("""
                ALTER TABLE IF EXISTS sg_city
                    ADD COLUMN IF NOT EXISTS extra_build_slots int NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS extra_field_slots int NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS extra_tech_slots int NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS extra_recruit_slots int NOT NULL DEFAULT 0
                """);

            orm.Ado.ExecuteNonQuery("""
                CREATE TABLE IF NOT EXISTS sg_recruit (
                    id bigserial PRIMARY KEY,
                    city_id int8 NOT NULL,
                    troop_type varchar(16) NOT NULL,
                    count int NOT NULL,
                    finish_at timestamp NOT NULL
                )
                """);

            orm.Ado.ExecuteNonQuery("""
                CREATE INDEX IF NOT EXISTS idx_recruit_city ON sg_recruit (city_id)
                """);

            orm.Ado.ExecuteNonQuery("""
                INSERT INTO sg_recruit (city_id, troop_type, count, finish_at)
                SELECT id, recruit_type, recruit_count, recruit_finish_at
                FROM sg_city
                WHERE recruit_type IS NOT NULL
                  AND recruit_finish_at IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM sg_recruit r
                    WHERE r.city_id = sg_city.id
                      AND r.troop_type = sg_city.recruit_type
                      AND r.count = sg_city.recruit_count
                      AND r.finish_at = sg_city.recruit_finish_at
                  )
                """);

            orm.Ado.ExecuteNonQuery("""
                UPDATE sg_city
                SET recruit_type = NULL, recruit_count = 0, recruit_finish_at = NULL
                WHERE recruit_type IS NOT NULL OR recruit_finish_at IS NOT NULL
                """);

            return orm;
        });

        return services;
    }
}
