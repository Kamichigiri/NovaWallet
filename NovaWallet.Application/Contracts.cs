namespace NovaWallet.Application.Contracts;

public sealed record CreateWalletRequest(string CustomerId);
public sealed record CreditWalletRequest(long AmountKobo);
public sealed record TransferRequest(Guid SourceWalletId, Guid DestinationWalletId, long AmountKobo);
public sealed record WalletResponse(Guid WalletId, string CustomerId, string Currency, long BalanceKobo);
public sealed record TransferResponse(Guid TransferId, string Status, long AmountKobo, string Currency, Guid SourceWalletId, Guid DestinationWalletId);
public sealed record StatementEntryResponse(Guid EntryId, string Direction, long AmountKobo, long BalanceAfterKobo, string Currency, DateTimeOffset CreatedAtUtc, Guid TransferId);
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);

public sealed record ProblemInfo(string Code, string Detail);

public interface IWalletService
{
    Task<WalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct);
    Task<WalletResponse> GetBalanceAsync(Guid walletId, CancellationToken ct);
    Task<WalletResponse> CreditAsync(Guid walletId, CreditWalletRequest request, CancellationToken ct);
    Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, CancellationToken ct);
    Task<PagedResult<StatementEntryResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly WatToday { get; }
}