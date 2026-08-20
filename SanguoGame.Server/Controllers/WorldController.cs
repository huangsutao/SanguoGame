using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/world")]
public sealed class WorldController : ControllerBase
{
    private readonly WorldService _world;

    public WorldController(WorldService world)
    {
        _world = world;
    }

    [HttpGet]
    public async Task<ApiResult<WorldDto>> Get(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _world.GetAsync(User.GetAccountId(), cancellationToken));
}
