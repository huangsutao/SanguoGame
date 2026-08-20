using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/markets")]
public sealed class MarketsController : ControllerBase
{
    private readonly TransportService _transports;

    public MarketsController(TransportService transports)
    {
        _transports = transports;
    }

    [HttpGet]
    public async Task<ApiResult<MarketsOverviewDto>> Get(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _transports.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("trade")]
    public async Task<ApiResult<MarketsOverviewDto>> Trade(
        [FromBody] MarketTradeRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _transports.TradeAsync(User.GetAccountId(), request, cancellationToken));

    [HttpPost("aid")]
    public async Task<ApiResult<MarketsOverviewDto>> Aid(
        [FromBody] MarketAidRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _transports.AidAsync(User.GetAccountId(), request, cancellationToken));
}
