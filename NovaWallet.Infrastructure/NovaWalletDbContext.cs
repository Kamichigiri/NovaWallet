using Microsoft.EntityFrameworkCore;
using NovaWallet.Domain.Entities;

namespace NovaWallet.Infrastructure;

public sealed class NovaWalletDbContext(DbContextOptions<NovaWalletDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Wallet>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CustomerId).IsUnique();
            e.Property(x => x.CustomerId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        });
        b.Entity<Transfer>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.SourceWalletId, x.CreatedAtUtc });
        });
        b.Entity<LedgerEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.WalletId, x.CreatedAtUtc });
            e.Property(x => x.Direction).HasMaxLength(10).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        });
        b.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAtUtc });
        });
        b.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.RequestHash).HasMaxLength(64);
        });
    }

    public async Task<Wallet?> LockWalletAsync(
        Guid walletId, CancellationToken ct)
    {
        return await Wallets
            .FromSqlInterpolated($"""
            SELECT Id, CustomerId, Currency, BalanceKobo,
                   DailyOutboundKobo, DailyOutboundDate, CreatedAtUtc
            FROM Wallets WITH (UPDLOCK, ROWLOCK)
                        WHERE Id = {walletId}
            """)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<List<Wallet>> LockWalletsAsync(
    Guid walletId1,
    Guid walletId2,
    CancellationToken ct)
    {
        return await Wallets
            .FromSqlInterpolated($"""
            SELECT Id, CustomerId, Currency, BalanceKobo,
                   DailyOutboundKobo, DailyOutboundDate, CreatedAtUtc
            FROM Wallets WITH (UPDLOCK, ROWLOCK)
            WHERE Id IN ({walletId1}, {walletId2})
            """)
            .ToListAsync(ct);
    }
}
