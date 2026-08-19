using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;

namespace SanguoGame.Server.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("ping")]
    public ApiResult<PingResponse> Ping() =>
        ApiResult.Ok(new PingResponse(DateTime.UtcNow));
}
