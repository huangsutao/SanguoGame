using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[Authorize]
[ApiController]
[Route("api/mail")]
public sealed class MailController : ControllerBase
{
    private readonly MailService _mail;

    public MailController(MailService mail)
    {
        _mail = mail;
    }

    [HttpGet]
    public async Task<ApiResult<MailListDto>> List(
        [FromQuery] PagedQuery query,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default) =>
        ApiResult.Ok(await _mail.ListAsync(User.GetAccountId(), query, unreadOnly, cancellationToken));

    [HttpPost("{id:long}/read")]
    public async Task<ApiResult<object?>> Read(long id, CancellationToken cancellationToken)
    {
        await _mail.ReadAsync(User.GetAccountId(), id, cancellationToken);
        return ApiResult.Ok();
    }

    [HttpPost("read-all")]
    public async Task<ApiResult<object?>> ReadAll(CancellationToken cancellationToken)
    {
        await _mail.ReadAllAsync(User.GetAccountId(), cancellationToken);
        return ApiResult.Ok();
    }
}
