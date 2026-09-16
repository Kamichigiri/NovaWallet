using NovaWallet.Domain.Entities;
using Xunit;

namespace NovaWallet.Tests;

public class WalletDomainTests
{
    [Fact]
    public void Debit_cannot_make_balance_negative()
    {
        var wallet = new Wallet(Guid.NewGuid(), "C001", DateTimeOffset.UtcNow);
        wallet.Credit(1_000);
        var ex = Assert.Throws<InvalidOperationException>(() => wallet.Debit(1_001, 50_000_000, DateOnly.FromDateTime(DateTime.UtcNow)));
        Assert.Equal("Insufficient funds.", ex.Message);
    }

    [Fact]
    public void Debit_tracks_daily_outbound()
    {
        var wallet = new Wallet(Guid.NewGuid(), "C001", DateTimeOffset.UtcNow);
        wallet.Credit(100_000);
        wallet.Debit(25_000, 50_000, new DateOnly(2026, 9, 15));
        Assert.Equal(75_000, wallet.BalanceKobo);
    }
}
