using Microsoft.EntityFrameworkCore;
using NovaWallet.Application.Contracts;
using NovaWallet.Application.Exceptions;
using NovaWallet.Infrastructure;
using Xunit;

namespace NovaWallet.Tests;

public sealed class ConcurrencyTests
{
    //[Fact]
    public async Task Concurrent_transfers_never_double_spend()
    {
        // Test Can Only Be Run with Live database, not InMemoryDatabase. InMemoryDatabase does not support transactions and concurrency control like a real database does.

        var connectionString = Environment.GetEnvironmentVariable("NOVAWALLET_TEST_DB")
            ?? "Server=localhost;Database=novawallet_test;Integrated Security=True;Encrypt=False;";

        var options = new DbContextOptionsBuilder<NovaWalletDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        await using (var setup = new NovaWalletDbContext(options))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.EnsureCreatedAsync();
        }

        var clock = new TestClock(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));
        Guid sourceId;
        Guid destinationId;

        await using (var db = new NovaWalletDbContext(options))
        {
            var service = new WalletService(db, clock);
            sourceId = (await service.CreateWalletAsync(new CreateWalletRequest("SOURCE"), default)).WalletId;
            destinationId = (await service.CreateWalletAsync(new CreateWalletRequest("DEST"), default)).WalletId;
        }

        await using (var db = new NovaWalletDbContext(options))
        {
            var service = new WalletService(db, clock);
            await service.CreditAsync(sourceId, new CreditWalletRequest(1_000_000), default);
        }

        const int concurrentRequests = 50;
        const long amount = 30_000;
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            await using var db = new NovaWalletDbContext(options);
            var service = new WalletService(db, clock);
            try
            {
                await service.TransferAsync(new TransferRequest(sourceId, destinationId, amount), $"concurrency-{i}", default);
                return true;
            }
            catch (ConflictAppException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(x => x);

        await using var verify = new NovaWalletDbContext(options);
        var source = await verify.Wallets.AsNoTracking().SingleAsync(x => x.Id == sourceId);
        var transfers = await verify.Transfers.CountAsync(x => x.SourceWalletId == sourceId);

        Assert.True(source.BalanceKobo >= 0);
        Assert.Equal(successCount, transfers);
        Assert.Equal(1_000_000 - (successCount * amount), source.BalanceKobo);
        Assert.InRange(successCount, 0, 33);
    }

    private sealed class TestClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
        public DateOnly WatToday => new(2026, 9, 15);
    }
}
