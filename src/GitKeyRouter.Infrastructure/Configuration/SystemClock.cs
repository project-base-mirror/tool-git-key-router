using GitKeyRouter.Core.Abstractions;

namespace GitKeyRouter.Infrastructure.Configuration;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => DateTimeOffset.Now;
}
