namespace NovaWallet.Domain.Entities;

public sealed class Wallet
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = null!;
    public string Currency { get; private set; } = "NGN";
    public long BalanceKobo { get; private set; }
    public long DailyOutboundKobo { get; private set; }
    public DateOnly DailyOutboundDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Wallet() { }

    public Wallet(Guid id, string customerId, DateTimeOffset createdAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        Currency = "NGN";
        BalanceKobo = 0;
        DailyOutboundKobo = 0;
        DailyOutboundDate = DateOnly.FromDateTime(createdAtUtc.UtcDateTime);
        CreatedAtUtc = createdAtUtc;
    }

    public void Credit(long amountKobo)
    {
        if (amountKobo <= 0) throw new ArgumentOutOfRangeException(nameof(amountKobo));
        BalanceKobo = checked(BalanceKobo + amountKobo);
    }

    public void Debit(long amountKobo, long dailyLimitKobo, DateOnly watDate)
    {
        if (amountKobo <= 0) throw new ArgumentOutOfRangeException(nameof(amountKobo));
        if (BalanceKobo < amountKobo) throw new InvalidOperationException("Insufficient funds.");
        if (DailyOutboundDate != watDate) DailyOutboundKobo = 0;
        if (amountKobo > dailyLimitKobo || DailyOutboundKobo > dailyLimitKobo - amountKobo) throw new InvalidOperationException("Daily outbound transfer limit exceeded.");
        BalanceKobo = checked(BalanceKobo - amountKobo);
        DailyOutboundKobo = checked(DailyOutboundKobo + amountKobo);
        DailyOutboundDate = watDate;
    }
}

public sealed class Transfer
{
    public Guid Id { get; set; }
    public Guid SourceWalletId { get; set; }
    public Guid DestinationWalletId { get; set; }
    public long AmountKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Status { get; set; } = "Completed";
    public string IdempotencyKey { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class LedgerEntry
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public Guid TransferId { get; set; }
    public string Direction { get; set; } = null!;
    public long AmountKobo { get; set; }
    public long BalanceAfterKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class AuditLog
{
    public long Id { get; set; }
    public string EventType { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public Guid EntityId { get; set; }
    public string PayloadJson { get; set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public Guid TransferId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
