namespace GitKeyRouter.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateTimeOffset LocalNow { get; }
}
