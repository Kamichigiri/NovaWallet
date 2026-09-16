using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaWallet.Application.Contracts;
using NovaWallet.Application.Exceptions;

namespace NovaWallet.Api.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize]
public sealed class TransfersController(IWalletService service) : ControllerBase
{
    [HttpPost]    
    public async Task<ActionResult<TransferResponse>> Post(TransferRequest request, CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyValues) ||
            string.IsNullOrEmpty(idempotencyValues))
        {
            throw new ValidationAppException("Idempotency-Key header is required for transfer requests.");
        }

        var key = idempotencyValues.ToString();
        var result = await service.TransferAsync(request, key, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
