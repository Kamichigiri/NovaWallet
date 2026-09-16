using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Contracts;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/wallets")]
[Authorize]
public sealed class WalletsController(IWalletService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<WalletResponse>> Create(CreateWalletRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await service.CreateWalletAsync(request, ct));

    [HttpGet("{walletId:guid}/balance")]
    public Task<WalletResponse> Balance(Guid walletId, CancellationToken ct) => service.GetBalanceAsync(walletId, ct);

    [HttpPost("{walletId:guid}/credit")]
    public async Task<WalletResponse> Credit(Guid walletId, CreditWalletRequest request, CancellationToken ct)
        => await service.CreditAsync(walletId, request, ct);

    [HttpGet("{walletId:guid}/statement")]
    public Task<PagedResult<StatementEntryResponse>> Statement(Guid walletId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => service.GetStatementAsync(walletId, page, pageSize, ct);
}
