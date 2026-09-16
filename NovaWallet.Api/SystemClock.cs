using NovaWallet.Application.Contracts;

namespace NovaWallet.Api;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly WatToday => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Africa/Lagos").Date);
}
