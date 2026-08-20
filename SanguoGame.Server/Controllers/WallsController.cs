using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/walls")]
public sealed class WallsController : ControllerBase
{
    private readonly WallService _walls;

    public WallsController(WallService walls)
    {
        _walls = walls;
    }

    [HttpGet]
    public async Task<ApiResult<WallsOverviewDto>> List(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _walls.GetOverviewAsync(User.GetAccountId(), cancellationToken));

    [HttpPost("upgrade")]
    public async Task<ApiResult<WallsOverviewDto>> Upgrade(
        [FromBody] UpgradeWallRequest request,
        CancellationToken cancellationToken) =>
        ApiResult.Ok(await _walls.UpgradeAsync(User.GetAccountId(), request.WallType, cancellationToken));
}
