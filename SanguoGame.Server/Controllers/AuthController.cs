using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SanguoGame.Server.Contracts;
using SanguoGame.Server.Security;
using SanguoGame.Server.Services;

namespace SanguoGame.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ApiResult<TokenResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken) =>
        ApiResult.Ok(await _auth.RegisterAsync(request, cancellationToken));

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ApiResult<TokenResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken) =>
        ApiResult.Ok(await _auth.LoginAsync(request, cancellationToken));

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<ApiResult<TokenResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken) =>
        ApiResult.Ok(await _auth.RefreshAsync(request, cancellationToken));

    [HttpPost("logout")]
    [EnableRateLimiting("auth")]
    public async Task<ApiResult<object?>> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(request, cancellationToken);
        return ApiResult.Ok();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ApiResult<SessionResponse>> Me(CancellationToken cancellationToken) =>
        ApiResult.Ok(await _auth.GetSessionAsync(User.GetAccountId(), cancellationToken));
}
