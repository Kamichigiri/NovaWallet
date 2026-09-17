using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Contracts;
using NovaWallet.Application.Exceptions;
using NovaWallet.Domain.Entities;
using NovaWallet.Infrastructure;
using Xunit;

namespace NovaWallet.Tests;

public class WalletServiceTests
{
    private static DbContextOptions<NovaWalletDbContext> CreateOptions(string dbName)
    {
        return new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
    }
    private sealed class TestClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly WatToday => DateOnly.FromDateTime(now.Date);
    }

    [Fact]
    public async Task CreateWalletAsync_creates_wallet()
    {
        var options = CreateOptions(nameof(CreateWalletAsync_creates_wallet));
        var clock = new TestClock(DateTimeOffset.UtcNow);

        await using (var db = new NovaWalletDbContext(options))
        {
            var svc = new WalletService(db, clock);
            var resp = await svc.CreateWalletAsync(new CreateWalletRequest("CUST-001"), CancellationToken.None);

            Assert.NotEqual(Guid.Empty, resp.WalletId);
            Assert.Equal("CUST-001", resp.CustomerId);
            Assert.Equal(0, resp.BalanceKobo);
        }

        await using (var db = new NovaWalletDbContext(options))
        {
            var stored = await db.Wallets.AsNoTracking().SingleAsync();
            Assert.Equal("CUST-001", stored.CustomerId);
            Assert.Equal(0, stored.BalanceKobo);
        }
    }

    [Fact]
    public async Task CreateWalletAsync_duplicate_throws_ConflictAppException()
    {
        var options = CreateOptions(nameof(CreateWalletAsync_duplicate_throws_ConflictAppException));
        var clock = new TestClock(DateTimeOffset.UtcNow);

        await using (var db = new NovaWalletDbContext(options))
        {
            var svc = new WalletService(db, clock);
            await svc.CreateWalletAsync(new CreateWalletRequest("DUP-001"), CancellationToken.None);

            await Assert.ThrowsAsync<ConflictAppException>(async () =>
                await svc.CreateWalletAsync(new CreateWalletRequest("DUP-001"), CancellationToken.None));
        }
    }

    [Fact]
    public async Task GetBalanceAsync_not_found_throws_NotFoundAppException()
    {
        var options = CreateOptions(nameof(GetBalanceAsync_not_found_throws_NotFoundAppException));
        var clock = new TestClock(DateTimeOffset.UtcNow);

        await using (var db = new NovaWalletDbContext(options))
        {
            var svc = new WalletService(db, clock);
            await Assert.ThrowsAsync<NotFoundAppException>(async () =>
                await svc.GetBalanceAsync(Guid.NewGuid(), CancellationToken.None));
        }
    }

    [Fact]
    public async Task GetStatementAsync_returns_paged_result()
    {
        var options = CreateOptions(nameof(GetStatementAsync_returns_paged_result));
        var clock = new TestClock(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

        Guid walletId;
        await using (var db = new NovaWalletDbContext(options))
        {
            var svc = new WalletService(db, clock);
            walletId = (await svc.CreateWalletAsync(new CreateWalletRequest("STMT-001"), CancellationToken.None)).WalletId;
        }

        // create several ledger entries directly
        await using (var db = new NovaWalletDbContext(options))
        {
            for (var i = 0; i < 25; i++)
            {
                db.LedgerEntries.Add(new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    WalletId = walletId,
                    Direction = i % 2 == 0 ? "Credit" : "Debit",
                    AmountKobo = 1_000 + i,
                    BalanceAfterKobo = 1_000 + i,
                    Currency = "NGN",
                    CreatedAtUtc = clock.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        await using (var db = new NovaWalletDbContext(options))
        {
            var svc = new WalletService(db, clock);
            var page1 = await svc.GetStatementAsync(walletId, page: 1, pageSize: 10, CancellationToken.None);

            Assert.Equal(10, page1.Items.Count);
            Assert.Equal(1, page1.Page);
            Assert.Equal(10, page1.PageSize);
            Assert.Equal(25, page1.TotalCount);

            var page3 = await svc.GetStatementAsync(walletId, page: 3, pageSize: 10, CancellationToken.None);
            Assert.Equal(5, page3.Items.Count);
            Assert.Equal(3, page3.Page);
        }
    }
}