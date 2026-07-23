using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace GitKeyRouter.App;

public sealed class SingleInstanceGuard : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> OwnedMutexes = new(StringComparer.Ordinal);

    private readonly string? _mutexName;
    private readonly Mutex? _mutex;
    private bool _disposed;

    private SingleInstanceGuard(string? mutexName, Mutex? mutex, bool isPrimaryInstance)
    {
        _mutexName = mutexName;
        _mutex = mutex;
        IsPrimaryInstance = isPrimaryInstance;
    }

    public bool IsPrimaryInstance { get; }

    public static SingleInstanceGuard TryAcquire(string? mutexName = null)
    {
        var resolvedName = string.IsNullOrWhiteSpace(mutexName)
            ? CreateDefaultMutexName()
            : mutexName.Trim();

        if (!OwnedMutexes.TryAdd(resolvedName, 0))
        {
            return new SingleInstanceGuard(null, null, false);
        }

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, resolvedName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                OwnedMutexes.TryRemove(resolvedName, out _);
                return new SingleInstanceGuard(null, null, false);
            }

            return new SingleInstanceGuard(resolvedName, mutex, true);
        }
        catch
        {
            mutex?.Dispose();
            OwnedMutexes.TryRemove(resolvedName, out _);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!IsPrimaryInstance || _mutex is null || _mutexName is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        finally
        {
            _mutex.Dispose();
            OwnedMutexes.TryRemove(_mutexName, out _);
        }
    }

    private static string CreateDefaultMutexName()
    {
        var applicationDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var scope = string.IsNullOrWhiteSpace(applicationDataPath)
            ? $"{Environment.UserDomainName}\\{Environment.UserName}"
            : Path.GetFullPath(applicationDataPath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
        return $@"Local\GitKeyRouter.{hash[..16]}";
    }
}
