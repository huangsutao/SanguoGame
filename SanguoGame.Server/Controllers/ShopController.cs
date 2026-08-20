using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/shop")]
public sealed class ShopController : ControllerBase
{
    private readonly ShopService _shop;

    public ShopController(ShopService shop)
    {
        _shop = shop;
    }

    [HttpGet]
    public async Task<ApiResult<ShopOverviewDto>> Get(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _shop.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("buy")]
    public async Task<ApiResult<ShopOverviewDto>> Buy(
        [FromBody] ShopBuyRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _shop.BuyAsync(User.GetAccountId(), request, cancellationToken));

    [HttpPost("use")]
    public async Task<ApiResult<ShopOverviewDto>> Use(
        [FromBody] ShopUseRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _shop.UseAsync(User.GetAccountId(), request, cancellationToken));
}
