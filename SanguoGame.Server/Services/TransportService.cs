using System.Data.Common;
using FreeSql;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SanguoGame.Core;
using SanguoGame.Core.Army;
using SanguoGame.Core.Buildings;
using SanguoGame.Core.Market;
using SanguoGame.Core.Social;
using SanguoGame.Core.World;
using SanguoGame.Infrastructure.Entities;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Hubs;
using SanguoGame.Server.Jobs;

namespace SanguoGame.Server.Services;

public sealed class TransportService
{
    private readonly IFreeSql _orm;
    private readonly IBackgroundJobClient _jobs;
    private readonly IHubContext<GameHub> _hub;
    private readonly WorldMapOptions _map;
    private readonly ArmyService _army;
    private readonly MailService _mail;
    private readonly AllianceService _alliances;

    public TransportService(
        IFreeSql orm,
        IBackgroundJobClient jobs,
        IHubContext<GameHub> hub,
        IOptions<WorldMapOptions> map,
        ArmyService army,
        MailService mail,
        AllianceService alliances)
    {
        _orm = orm;
        _jobs = jobs;
        _hub = hub;
        _map = map.Value;
        _army = army;
        _mail = mail;
        _alliances = alliances;
    }

    public async Task<MarketsOverviewDto> GetOverviewAsync(long accountId, CancellationToken cancellationToken)
    {
        var city = await _army.RequireCityAsync(accountId, cancellationToken);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<MarketsOverviewDto> TradeAsync(long accountId, MarketTradeRequest request, CancellationToken cancellationToken)
    {
        if (!MarketCatalog.IsResource(request.FromResource) || !MarketCatalog.IsResource(request.ToResource))
        {
            throw new BizException(ErrorCodes.InvalidTrade, "未知资源类型");
        }

        var fromResource = MarketCatalog.Normalize(request.FromResource);
        var toResource = MarketCatalog.Normalize(request.ToResource);
        if (fromResource == toResource)
        {
            throw new BizException(ErrorCodes.InvalidTrade, "不能用同种资源兑换");
        }

        var creditAmount = MarketCatalog.Quote(fromResource, toResource, request.Amount);
        if (creditAmount < 1)
        {
            throw new BizException(ErrorCodes.InvalidTrade, "兑换数量过小或无法换得资源");
        }

        var city = await _army.RequireCityAsync(accountId, cancellationToken);
        var market = await _orm.Select<MarketEntity>().Where(m => m.Id == request.MarketId).FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "市集不存在");

        var pay = ResourceAmount.Zero.Add(fromResource, request.Amount);
        var credit = ResourceAmount.Zero.Add(toResource, creditAmount);
        var oneWay = MarchTiming.DurationSeconds(city.X, city.Y, market.X, market.Y, _map.SecondsPerTile, _map.MinMarchSeconds);

        var transportId = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            await EnsureCapacityAsync(transaction, locked, pay.Total, ct);
            Deduct(locked, pay);
            await SaveStockAsync(transaction, locked, ct);
            return await InsertTransportAsync(
                transaction,
                locked,
                TransportKind.Market,
                toCityId: 0,
                targetId: market.Id,
                toX: market.X,
                toY: market.Y,
                pay,
                credit,
                TimeSpan.FromSeconds(oneWay * 2),
                ct);
        }, cancellationToken);

        city = await ReloadCityAsync(city.Id, cancellationToken);
        Schedule(transportId);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task<MarketsOverviewDto> AidAsync(long accountId, MarketAidRequest request, CancellationToken cancellationToken)
    {
        var pay = new ResourceAmount(request.Grain, request.Wood, request.Iron, request.Copper);
        if (pay.Total <= 0)
        {
            throw new BizException(ErrorCodes.InvalidTrade, "至少运输一种资源");
        }

        var city = await _army.RequireCityAsync(accountId, cancellationToken);
        if (request.TargetCityId == city.Id)
        {
            throw new BizException(ErrorCodes.CannotAidSelf, "不能运给自己");
        }

        var inAlliance = await _orm.Select<AllianceMemberEntity>()
            .AnyAsync(m => m.CharacterId == city.CharacterId, cancellationToken);
        if (!inAlliance)
        {
            throw new BizException(ErrorCodes.NotInAlliance, "未加入联盟");
        }

        var target = await _orm.Select<CityEntity>().Where(c => c.Id == request.TargetCityId).FirstAsync(cancellationToken)
            ?? throw new BizException(ErrorCodes.NotFound, "目标城不存在");
        if (!await _alliances.AreAlliedByCityAsync(city.Id, target.Id, cancellationToken))
        {
            throw new BizException(ErrorCodes.NotAlliedTransport, "非同联盟不可运输");
        }

        var oneWay = MarchTiming.DurationSeconds(city.X, city.Y, target.X, target.Y, _map.SecondsPerTile, _map.MinMarchSeconds);
        var transportId = await CityRowLock.RunAsync(_orm, city.Id, async (transaction, locked, ct) =>
        {
            await EnsureCapacityAsync(transaction, locked, pay.Total, ct);
            Deduct(locked, pay);
            await SaveStockAsync(transaction, locked, ct);
            return await InsertTransportAsync(
                transaction,
                locked,
                TransportKind.Aid,
                toCityId: target.Id,
                targetId: target.Id,
                toX: target.X,
                toY: target.Y,
                pay,
                credit: pay,
                TimeSpan.FromSeconds(oneWay),
                ct);
        }, cancellationToken);

        city = await ReloadCityAsync(city.Id, cancellationToken);
        Schedule(transportId);
        return await BuildOverviewAsync(city, cancellationToken);
    }

    public async Task CompleteAsync(long transportId, CancellationToken cancellationToken)
    {
        var transport = await _orm.Select<TransportEntity>().Where(t => t.Id == transportId).FirstAsync(cancellationToken);
        if (transport is null || transport.Status != TransportStatus.InTransit)
        {
            return;
        }

        if (transport.ArriveAt > DateTime.UtcNow.AddSeconds(2))
        {
            _jobs.Schedule<CompleteTransportJob>(job => job.Execute(transportId), UtcSchedule.At(transport.ArriveAt));
            return;
        }

        TransportCompleteDto originResult;
        TransportCompleteDto? destResult = null;
        long? destCityId = null;

        if (transport.Kind == TransportKind.Market)
        {
            originResult = await SettleMarketAsync(transport, cancellationToken);
        }
        else
        {
            (originResult, destResult, destCityId) = await SettleAidAsync(transport, cancellationToken);
        }

        await _hub.Clients.Group($"city:{transport.FromCityId}")
            .SendAsync("TransportArrived", ApiResult.Ok(originResult), cancellationToken);
        if (destResult is not null && destCityId is long destId)
        {
            await _hub.Clients.Group($"city:{destId}")
                .SendAsync("ResourceReceived", ApiResult.Ok(destResult), cancellationToken);
        }
    }

    public async Task RecoverDueAsync(CancellationToken cancellationToken)
    {
        var due = await _orm.Select<TransportEntity>()
            .Where(t => t.Status == TransportStatus.InTransit && t.ArriveAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);
        foreach (var transport in due)
        {
            await CompleteAsync(transport.Id, cancellationToken);
        }
    }

    internal static TransportDto MapTransport(TransportEntity row, bool mine) =>
        new(
            row.Id,
            row.Kind,
            row.FromCityId,
            row.ToCityId,
            row.TargetId,
            row.FromX,
            row.FromY,
            row.ToX,
            row.ToY,
            ToDto(PayOf(row)),
            ToDto(CreditOf(row)),
            row.DepartAt,
            row.ArriveAt,
            row.Status,
            mine);

    private async Task<TransportCompleteDto> SettleMarketAsync(TransportEntity transport, CancellationToken cancellationToken)
    {
        return await CityRowLock.RunAsync(_orm, transport.FromCityId, async (transaction, city, ct) =>
        {
            var current = await LoadTransportAsync(transaction, transport.Id, ct);
            if (current is null || current.Status != TransportStatus.InTransit)
            {
                return await EmptyCompleteAsync(city, transport.Id, TransportKind.Market, "运输已结算", ct);
            }

            var market = await _orm.Select<MarketEntity>()
                .WithTransaction(transaction)
                .Where(m => m.Id == current.TargetId)
                .FirstAsync(ct);
            var incoming = market is null ? PayOf(current) : CreditOf(current);
            var title = market is null ? "市集货物退回" : "市集兑换完成";
            var summaryPrefix = market is null
                ? $"市集已消失，退回{Format(incoming)}"
                : $"兑换完成，获得{Format(incoming)}";
            return await FinishDepositAsync(transaction, current, city, incoming, title, summaryPrefix, city.CharacterId, ct);
        }, cancellationToken);
    }

    private async Task<(TransportCompleteDto Origin, TransportCompleteDto? Dest, long? DestCityId)> SettleAidAsync(
        TransportEntity transport,
        CancellationToken cancellationToken)
    {
        var destExists = await _orm.Select<CityEntity>().AnyAsync(c => c.Id == transport.ToCityId, cancellationToken);
        if (!destExists || transport.ToCityId <= 0)
        {
            var origin = await CityRowLock.RunAsync(_orm, transport.FromCityId, async (transaction, city, ct) =>
            {
                var current = await LoadTransportAsync(transaction, transport.Id, ct);
                if (current is null || current.Status != TransportStatus.InTransit)
                {
                    return await EmptyCompleteAsync(city, transport.Id, TransportKind.Aid, "运输已结算", ct);
                }

                return await FinishDepositAsync(
                    transaction,
                    current,
                    city,
                    PayOf(current),
                    "援助退回",
                    $"目标城已消失，退回{Format(PayOf(current))}",
                    city.CharacterId,
                    ct);
            }, cancellationToken);
            return (origin, null, null);
        }

        return await CityRowLock.RunTwoAsync(
            _orm,
            transport.FromCityId,
            transport.ToCityId,
            async (transaction, fromCity, toCity, ct) =>
            {
                var current = await LoadTransportAsync(transaction, transport.Id, ct);
                if (current is null || current.Status != TransportStatus.InTransit)
                {
                    var originDone = await EmptyCompleteAsync(fromCity, transport.Id, TransportKind.Aid, "运输已结算", ct);
                    return (originDone, (TransportCompleteDto?)null, (long?)null);
                }

                var incoming = CreditOf(current);
                var destComplete = await FinishDepositAsync(
                    transaction,
                    current,
                    toCity,
                    incoming,
                    "收到同盟资源",
                    $"来自{fromCity.Name}，获得{Format(incoming)}",
                    toCity.CharacterId,
                    ct,
                    extraRecipient: fromCity.CharacterId,
                    extraTitle: "资源已送达",
                    extraBody: $"已送达{toCity.Name}：{Format(incoming)}");

                var fromCap = await ResourceCapAsync(transaction, fromCity.Id, ct);
                var origin = new TransportCompleteDto(
                    current.Id,
                    current.Kind,
                    destComplete.Credited,
                    destComplete.Overflow,
                    ToDto(CityStats.Stock(fromCity)),
                    fromCap,
                    $"已送达{toCity.Name}，对方入库{Format(FromDto(destComplete.Credited))}");
                return (origin, destComplete, toCity.Id);
            },
            cancellationToken);
    }

    private async Task<TransportCompleteDto> FinishDepositAsync(
        DbTransaction transaction,
        TransportEntity transport,
        CityEntity city,
        ResourceAmount incoming,
        string mailTitle,
        string summaryPrefix,
        long mailRecipient,
        CancellationToken cancellationToken,
        long? extraRecipient = null,
        string? extraTitle = null,
        string? extraBody = null)
    {
        transport.Status = TransportStatus.Settled;
        var updated = await _orm.Update<TransportEntity>()
            .WithTransaction(transaction)
            .Where(t => t.Id == transport.Id && t.Status == TransportStatus.InTransit)
            .Set(t => t.Status, TransportStatus.Settled)
            .ExecuteAffrowsAsync(cancellationToken);
        if (updated != 1)
        {
            return await EmptyCompleteAsync(city, transport.Id, transport.Kind, "运输已结算", cancellationToken);
        }

        var cap = await ResourceCapAsync(transaction, city.Id, cancellationToken);
        var credited = Deposit(city, incoming, cap);
        var overflow = incoming.Subtract(credited);
        await SaveStockAsync(transaction, city, cancellationToken);

        var overflowText = overflow.Total > 0 ? $"，仓库已满溢出{Format(overflow)}" : "";
        var summary = summaryPrefix + overflowText;
        await _mail.SendAsync(
            mailRecipient,
            MailType.System,
            mailTitle,
            summary,
            "transport",
            transport.Id,
            cancellationToken,
            transaction);
        if (extraRecipient is long extraId && extraTitle is not null && extraBody is not null)
        {
            await _mail.SendAsync(
                extraId,
                MailType.System,
                extraTitle,
                extraBody + overflowText,
                "transport",
                transport.Id,
                cancellationToken,
                transaction);
        }

        return new TransportCompleteDto(
            transport.Id,
            transport.Kind,
            ToDto(credited),
            ToDto(overflow),
            ToDto(CityStats.Stock(city)),
            cap,
            summary);
    }

    private async Task<MarketsOverviewDto> BuildOverviewAsync(CityEntity city, CancellationToken cancellationToken)
    {
        var buildings = await _orm.Select<BuildingEntity>().Where(b => b.CityId == city.Id).ToListAsync(cancellationToken);
        var warehouseLevel = buildings.FirstOrDefault(b => b.Type == "warehouse")?.Level ?? 0;
        var resourceCap = InnerBuildingCatalog.ResourceCap(warehouseLevel);
        var cargoCap = MarketCatalog.CargoCap(warehouseLevel);
        var markets = await _orm.Select<MarketEntity>().OrderBy(m => m.Id).ToListAsync(cancellationToken);
        var marketDtos = markets.Select(market =>
        {
            var oneWay = MarchTiming.DurationSeconds(
                city.X, city.Y, market.X, market.Y, _map.SecondsPerTile, _map.MinMarchSeconds);
            return new MarketItemDto(market.Id, market.Name, market.X, market.Y, oneWay, oneWay * 2);
        }).ToList();

        var transports = await _orm.Select<TransportEntity>()
            .Where(t => t.Status == TransportStatus.InTransit
                && (t.FromCityId == city.Id || (t.Kind == TransportKind.Aid && t.ToCityId == city.Id)))
            .OrderBy(t => t.ArriveAt)
            .ToListAsync(cancellationToken);

        var rates = new List<MarketRateDto>();
        foreach (var from in MarketCatalog.Resources)
        {
            foreach (var to in MarketCatalog.Resources)
            {
                if (from == to)
                {
                    continue;
                }

                rates.Add(new MarketRateDto(
                    from,
                    to,
                    MarketCatalog.QuoteSampleAmount,
                    MarketCatalog.Quote(from, to, MarketCatalog.QuoteSampleAmount)));
            }
        }

        return new MarketsOverviewDto(
            city.Id,
            DateTime.UtcNow,
            ToDto(CityStats.Stock(city)),
            resourceCap,
            cargoCap,
            MarketCatalog.TaxRate,
            MarketCatalog.MinAmount,
            new MarketValueDto(
                MarketCatalog.Value("grain"),
                MarketCatalog.Value("wood"),
                MarketCatalog.Value("iron"),
                MarketCatalog.Value("copper")),
            rates,
            marketDtos,
            transports.Select(t => MapTransport(t, t.FromCityId == city.Id)).ToList());
    }

    private async Task EnsureCapacityAsync(DbTransaction transaction, CityEntity city, int cargoTotal, CancellationToken cancellationToken)
    {
        var marchingCount = (int)await _orm.Select<TransportEntity>()
            .WithTransaction(transaction)
            .Where(t => t.FromCityId == city.Id && t.Status == TransportStatus.InTransit)
            .CountAsync(cancellationToken);
        if (marchingCount >= _map.MaxTransportsPerCity)
        {
            throw new BizException(ErrorCodes.TransportLimit, "运输数量已达上限");
        }

        var warehouseLevel = (await LoadBuildingsAsync(transaction, city.Id, cancellationToken))
            .FirstOrDefault(b => b.Type == "warehouse")?.Level ?? 0;
        var cargoCap = MarketCatalog.CargoCap(warehouseLevel);
        if (cargoTotal > cargoCap)
        {
            throw new BizException(ErrorCodes.InvalidTrade, $"单次运量不能超过 {cargoCap}");
        }
    }

    private async Task<long> InsertTransportAsync(
        DbTransaction transaction,
        CityEntity city,
        TransportKind kind,
        long toCityId,
        long targetId,
        int toX,
        int toY,
        ResourceAmount pay,
        ResourceAmount credit,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var row = new TransportEntity
        {
            Kind = kind,
            FromCityId = city.Id,
            ToCityId = toCityId,
            TargetId = targetId,
            FromX = city.X,
            FromY = city.Y,
            ToX = toX,
            ToY = toY,
            PayGrain = pay.Grain,
            PayWood = pay.Wood,
            PayIron = pay.Iron,
            PayCopper = pay.Copper,
            CreditGrain = credit.Grain,
            CreditWood = credit.Wood,
            CreditIron = credit.Iron,
            CreditCopper = credit.Copper,
            DepartAt = now,
            ArriveAt = now.Add(duration),
            Status = TransportStatus.InTransit
        };
        return await _orm.Insert(row).WithTransaction(transaction).ExecuteIdentityAsync(cancellationToken);
    }

    private void Deduct(CityEntity city, ResourceAmount pay)
    {
        var stock = CityStats.Stock(city);
        var missing = stock.FirstMissingAgainst(pay);
        if (missing is not null)
        {
            throw new BizException(ErrorCodes.InsufficientResources, $"{missing}不足");
        }

        CityStats.ApplyStock(city, stock.Subtract(pay));
    }

    private void Schedule(long transportId)
    {
        var stored = _orm.Select<TransportEntity>().Where(t => t.Id == transportId).First();
        if (stored is not null)
        {
            _jobs.Schedule<CompleteTransportJob>(job => job.Execute(stored.Id), UtcSchedule.At(stored.ArriveAt));
        }
    }

    private async Task<CityEntity> ReloadCityAsync(long cityId, CancellationToken cancellationToken) =>
        await _orm.Select<CityEntity>().Where(c => c.Id == cityId).FirstAsync(cancellationToken)
        ?? throw new BizException(ErrorCodes.NotFound, "尚未建立主城");

    private async Task<TransportEntity?> LoadTransportAsync(DbTransaction transaction, long id, CancellationToken cancellationToken) =>
        await _orm.Select<TransportEntity>().WithTransaction(transaction).Where(t => t.Id == id).ToOneAsync(cancellationToken);

    private Task<List<BuildingEntity>> LoadBuildingsAsync(DbTransaction transaction, long cityId, CancellationToken cancellationToken) =>
        _orm.Select<BuildingEntity>().WithTransaction(transaction).Where(b => b.CityId == cityId).ToListAsync(cancellationToken);

    private async Task<int> ResourceCapAsync(DbTransaction transaction, long cityId, CancellationToken cancellationToken)
    {
        var warehouse = (await LoadBuildingsAsync(transaction, cityId, cancellationToken))
            .FirstOrDefault(b => b.Type == "warehouse")?.Level ?? 0;
        return InnerBuildingCatalog.ResourceCap(warehouse);
    }

    private async Task SaveStockAsync(DbTransaction transaction, CityEntity city, CancellationToken cancellationToken) =>
        await _orm.Update<CityEntity>()
            .WithTransaction(transaction)
            .SetSource(city)
            .UpdateColumns(c => new { c.Grain, c.Wood, c.Iron, c.Copper })
            .ExecuteAffrowsAsync(cancellationToken);

    private async Task<TransportCompleteDto> EmptyCompleteAsync(
        CityEntity city,
        long transportId,
        TransportKind kind,
        string summary,
        CancellationToken cancellationToken)
    {
        var warehouse = await _orm.Select<BuildingEntity>()
            .Where(b => b.CityId == city.Id && b.Type == "warehouse")
            .FirstAsync(cancellationToken);
        var cap = InnerBuildingCatalog.ResourceCap(warehouse?.Level ?? 0);
        return new TransportCompleteDto(
            transportId,
            kind,
            ToDto(ResourceAmount.Zero),
            ToDto(ResourceAmount.Zero),
            ToDto(CityStats.Stock(city)),
            cap,
            summary);
    }

    private static ResourceAmount Deposit(CityEntity city, ResourceAmount loot, int cap)
    {
        var space = new ResourceAmount(
            Math.Max(0, cap - city.Grain),
            Math.Max(0, cap - city.Wood),
            Math.Max(0, cap - city.Iron),
            Math.Max(0, cap - city.Copper));
        var actual = loot.Min(space);
        CityStats.ApplyStock(city, CityStats.Stock(city).Add(actual));
        return actual;
    }

    private static ResourceAmount PayOf(TransportEntity row) =>
        new(row.PayGrain, row.PayWood, row.PayIron, row.PayCopper);

    private static ResourceAmount CreditOf(TransportEntity row) =>
        new(row.CreditGrain, row.CreditWood, row.CreditIron, row.CreditCopper);

    private static ResourceDto ToDto(ResourceAmount amount) =>
        new(amount.Grain, amount.Wood, amount.Iron, amount.Copper);

    private static ResourceAmount FromDto(ResourceDto dto) =>
        new(dto.Grain, dto.Wood, dto.Iron, dto.Copper);

    private static string Format(ResourceAmount amount) =>
        $"粮{amount.Grain} 木{amount.Wood} 铁{amount.Iron} 铜{amount.Copper}";
}
