using System.Text.Json;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Infrastructure;
using SanguoGame.Server.Filters;
using SanguoGame.Server.Hubs;
using SanguoGame.Server.Jobs;
using SanguoGame.Server.Json;
using SanguoGame.Server.Security;
namespace SanguoGame.Server;

public class Program
{
    public static void Main(string[] args)
    {
        // Npgsql 6+ 拒绝把 Kind=UTC 写入 timestamp without time zone。
        // FreeSql 建表用的是无时区 timestamp，需恢复旧行为。
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddGameAuth(builder.Configuration);
        builder.Services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default")),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true
                }));
        builder.Services.AddHangfireServer();
        builder.Services.AddHostedService<GameBootHostedService>();
        builder.Services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                options.PayloadSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
            });
        builder.Services.AddOpenApi();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("web", policy =>
                policy.WithOrigins("http://localhost:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials());
        });

        builder.Services.AddControllers(options =>
            {
                options.Filters.Add<ApiExceptionFilter>();
                options.Filters.Add<ApiValidationFilter>();
                options.Filters.Add<ApiTraceIdResultFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
            });

        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = true;
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }
        else
        {
            app.UseHttpsRedirection();
        }
        app.UseCors("web");
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<GameHub>("/hubs/game");

        var aiTickMinutes = Math.Max(1, app.Configuration.GetValue("WorldMap:AiTickMinutes", 5));
        RecurringJob.AddOrUpdate<AiTickJob>(
            "ai-tick",
            job => job.Execute(),
            $"*/{aiTickMinutes} * * * *");

        app.Run();
    }
}
