using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Contracts;
using NovaWallet.Application.Exceptions;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure;

public sealed class WalletService(NovaWalletDbContext db, ISystemClock clock) : IWalletService
{
    private const int MaxPageSize = 100;
    private const long DailyLimitKobo = 50_000_000L; // ₦500,000/day

    public async Task<WalletResponse> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            Activity.Current?.AddTag("WalletService.CreateWalletAsync :::", "CustomerId is null or empty");
            throw new ValidationAppException("CustomerId is required.");
        }

        var exists = await db.Wallets.AnyAsync(x => x.CustomerId == request.CustomerId.Trim(), ct);
        if (exists)
        {
            Activity.Current?.AddTag("WalletService.CreateWalletAsync :::", $"Wallet already exists for CustomerId: {request.CustomerId}");
            throw new ConflictAppException("A wallet already exists for the customer.");
        }
        var wallet = new Wallet(Guid.NewGuid(), request.CustomerId.Trim(), clock.UtcNow);
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync(ct);
        Activity.Current?.AddTag("WalletService.CreateWalletAsync :::", $"Wallet created with Id: {wallet.Id} for CustomerId: {request.CustomerId}");
        return Map(wallet);
    }

    public async Task<WalletResponse> GetBalanceAsync(Guid walletId, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == walletId, ct);
            if(wallet == null)
        {
            Activity.Current?.AddTag("WalletService.GetBalanceAsync :::", $"Wallet not found for Id: {walletId}");
            throw new NotFoundAppException("Wallet not found.");
        }
        return Map(wallet);
    }

    public async Task<WalletResponse> CreditAsync(Guid walletId, CreditWalletRequest request, CancellationToken ct)
    {
        if (request.AmountKobo <= 0)
        {
            Activity.Current?.AddTag("WalletService.CreditAsync :::", $"Invalid AmountKobo: {request.AmountKobo} for WalletId: {walletId}");
            throw new ValidationAppException("AmountKobo must be greater than zero.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        var wallet = await db.LockWalletAsync(walletId, ct);
        if (wallet == null)
        {
            Activity.Current?.AddTag("WalletService.CreditAsync :::", $"Wallet not found for Id: {walletId}");
            throw new NotFoundAppException("Wallet not found.");
        }

        wallet.Credit(request.AmountKobo);
        var mutationId = Guid.NewGuid();

        Activity.Current?.AddTag("WalletService.CreditAsync :::", $"Crediting WalletId: {walletId} with AmountKobo: {request.AmountKobo}, MutationId: {mutationId}");

        db.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(), WalletId = wallet.Id, TransferId = mutationId,
            Direction = "Credit", AmountKobo = request.AmountKobo,
            BalanceAfterKobo = wallet.BalanceKobo, Currency = wallet.Currency,
            CreatedAtUtc = clock.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            EventType = "WalletCredited", EntityType = "Wallet", EntityId = wallet.Id,
            PayloadJson = JsonSerializer.Serialize(new { mutationId, request.AmountKobo }),
            CreatedAtUtc = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        Activity.Current?.AddTag("WalletService.CreditAsync :::", $"WalletId: {walletId} credited successfully. New BalanceKobo: {wallet.BalanceKobo}");
        return Map(wallet);
    }

    public async Task<TransferResponse> TransferAsync(TransferRequest request, string idempotencyKey, CancellationToken ct)
    {
        Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Initiating transfer from SourceWalletId: {request.SourceWalletId} to DestinationWalletId: {request.DestinationWalletId} with AmountKobo: {request.AmountKobo} and IdempotencyKey: {idempotencyKey}");
        ValidateTransfer(request, idempotencyKey);
        var hash = ComputeRequestHash(request);

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var newTransferId = Guid.NewGuid();
        Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Generated new TransferId: {newTransferId} for the transfer operation");

        // Has Unique Constraint on IdempotencyRecords.Key, so this will either insert or return 0 on conflict created from concurrency if the key already exists
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO IdempotencyRecords
                            ([Key], [RequestHash], [TransferId], [CreatedAtUtc])
                        SELECT
                            {idempotencyKey},
                            {hash},
                            {newTransferId},
                            {clock.UtcNow}
                        WHERE NOT EXISTS
                        (
                            SELECT 1
                            FROM IdempotencyRecords WITH (UPDLOCK, HOLDLOCK)
                            WHERE [Key] = {idempotencyKey}
                        );
                        """, ct);

        if (inserted == 0)
        {
            Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Idempotency-Key: {idempotencyKey} already exists. Fetching existing transfer details.");
            var existing = await db.IdempotencyRecords.AsNoTracking().SingleAsync(x => x.Key == idempotencyKey, ct);
            if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            {
                Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Idempotency-Key: {idempotencyKey} was used with a different payload. Existing RequestHash: {existing.RequestHash}, Current RequestHash: {hash}");
                throw new ConflictAppException("Idempotency-Key was already used with a different payload.");
            }
            var existingTransfer = await db.Transfers.AsNoTracking().SingleAsync(x => x.Id == existing.TransferId, ct);
            Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Returning existing transfer details for TransferId: {existingTransfer.Id} associated with Idempotency-Key: {idempotencyKey}");
            await tx.CommitAsync(ct);
            return Map(existingTransfer);
        }

        var walletIds = new[] { request.SourceWalletId, request.DestinationWalletId }.OrderBy(x => x).ToArray();
        var first = await db.LockWalletAsync(walletIds[0], ct) ?? throw new NotFoundAppException("Wallet not found.");
        var second = await db.LockWalletAsync(walletIds[1], ct) ?? throw new NotFoundAppException("Wallet not found.");
        var source = first.Id == request.SourceWalletId ? first : second;
        var destination = first.Id == request.DestinationWalletId ? first : second;

        if (source.Currency != destination.Currency)
        {
            Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Currency mismatch between SourceWalletId: {source.Id} (Currency: {source.Currency}) and DestinationWalletId: {destination.Id} (Currency: {destination.Currency})");
            throw new ConflictAppException("Wallet currencies must match.");
        }

        try
        {
            source.Debit(request.AmountKobo, DailyLimitKobo, clock.WatToday);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Insufficient", StringComparison.OrdinalIgnoreCase))
        {
            Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Insufficient funds in SourceWalletId: {source.Id}. Attempted to debit AmountKobo: {request.AmountKobo}, Current BalanceKobo: {source.BalanceKobo}");
            throw new ConflictAppException(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Daily", StringComparison.OrdinalIgnoreCase))
        {
            Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Daily limit exceeded for SourceWalletId: {source.Id}");
            throw new ConflictAppException(ex.Message);
        }

        destination.Credit(request.AmountKobo);

        Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Transfer operation successful. SourceWalletId: {source.Id} debited by AmountKobo: {request.AmountKobo}, New BalanceKobo: {source.BalanceKobo}. DestinationWalletId: {destination.Id} credited by AmountKobo: {request.AmountKobo}, New BalanceKobo: {destination.BalanceKobo}");
        var transfer = new Transfer
        {
            Id = newTransferId, SourceWalletId = source.Id, DestinationWalletId = destination.Id,
            AmountKobo = request.AmountKobo, Currency = source.Currency, Status = "Completed",
            IdempotencyKey = idempotencyKey, RequestHash = hash, CreatedAtUtc = clock.UtcNow
        };
        db.Transfers.Add(transfer);
        db.LedgerEntries.AddRange(
            new LedgerEntry { Id = Guid.NewGuid(), WalletId = source.Id, TransferId = transfer.Id, Direction = "Debit", AmountKobo = request.AmountKobo, BalanceAfterKobo = source.BalanceKobo, Currency = source.Currency, CreatedAtUtc = clock.UtcNow },
            new LedgerEntry { Id = Guid.NewGuid(), WalletId = destination.Id, TransferId = transfer.Id, Direction = "Credit", AmountKobo = request.AmountKobo, BalanceAfterKobo = destination.BalanceKobo, Currency = destination.Currency, CreatedAtUtc = clock.UtcNow }
        );

        Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Ledger entries created for TransferId: {transfer.Id}. SourceWalletId: {source.Id} debited and DestinationWalletId: {destination.Id} credited.");
        db.AuditLogs.Add(new AuditLog
        {
            EventType = "TransferCompleted", EntityType = "Transfer", EntityId = transfer.Id,
            PayloadJson = JsonSerializer.Serialize(new { request.SourceWalletId, request.DestinationWalletId, request.AmountKobo, idempotencyKey }),
            CreatedAtUtc = clock.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        Activity.Current?.AddTag("WalletService.TransferAsync :::", $"Transfer operation completed successfully for TransferId: {transfer.Id}. Transaction committed.");
        return Map(transfer);
    }

    public async Task<PagedResult<StatementEntryResponse>> GetStatementAsync(Guid walletId, int page, int pageSize, CancellationToken ct)
    {
        Activity.Current?.AddTag("WalletService.GetStatementAsync :::", $"Fetching statement for WalletId: {walletId}, Page: {page}, PageSize: {pageSize}");
        if (page < 1){
            Activity.Current?.AddTag("WalletService.GetStatementAsync :::", $"Invalid page number: {page}. Page must be >= 1.");
            throw new ValidationAppException("Page must be >= 1.");
        }   
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        if (!await db.Wallets.AnyAsync(x => x.Id == walletId, ct))
        {
            Activity.Current?.AddTag("WalletService.GetStatementAsync :::", $"Wallet not found for Id: {walletId}.");
            throw new NotFoundAppException("Wallet not found.");
        }

        var query = db.LedgerEntries.AsNoTracking().Where(x => x.WalletId == walletId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new StatementEntryResponse(x.Id, x.Direction, x.AmountKobo, x.BalanceAfterKobo, x.Currency, x.CreatedAtUtc, x.TransferId))
            .ToListAsync(ct);
        Activity.Current?.AddTag("WalletService.GetStatementAsync :::", $"Fetched {items.Count} statement entries for WalletId: {walletId}. Total entries: {total}.");
        return new PagedResult<StatementEntryResponse>(items, page, pageSize, total);
    }

    private static void ValidateTransfer(TransferRequest request, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ValidationAppException("Idempotency-Key header is required for transfers.");
        if (request.SourceWalletId == Guid.Empty || request.DestinationWalletId == Guid.Empty)
        {
            Activity.Current?.AddTag("WalletService.ValidateTransfer :::", $"Invalid wallet IDs. SourceWalletId: {request.SourceWalletId}, DestinationWalletId: {request.DestinationWalletId}, Idempotency-Key: {key}");
            throw new ValidationAppException("Source and destination wallet IDs are required.");
        }
        if (request.SourceWalletId == request.DestinationWalletId)
        {
            Activity.Current?.AddTag("WalletService.ValidateTransfer :::", $"Source and destination wallets must be different. SourceWalletId: {request.SourceWalletId}, DestinationWalletId: {request.DestinationWalletId}, Idempotency-Key: {key}");
            throw new ValidationAppException("Source and destination wallets must be different.");
        }
        if (request.AmountKobo <= 0)
        {
            Activity.Current?.AddTag("WalletService.ValidateTransfer :::", $"Invalid amount. AmountKobo: {request.AmountKobo}, Idempotency-Key: {key}");
            throw new ValidationAppException("AmountKobo must be greater than zero.");
        }
    }

    private static string ComputeRequestHash(TransferRequest request)
    {
        var payload = $"{request.SourceWalletId:N}|{request.DestinationWalletId:N}|{request.AmountKobo}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static WalletResponse Map(Wallet w) => new(w.Id, w.CustomerId, w.Currency, w.BalanceKobo);
    private static TransferResponse Map(Transfer t) => new(t.Id, t.Status, t.AmountKobo, t.Currency, t.SourceWalletId, t.DestinationWalletId);
}
