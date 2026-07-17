namespace GitKeyRouter.Core.Abstractions;

public interface ISafeLogger
{
    void Information(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}
