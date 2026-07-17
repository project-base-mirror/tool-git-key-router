namespace GitKeyRouter.Core.Models;

public class OperationResult
{
    protected OperationResult(bool success, string message, IReadOnlyList<string>? errors = null)
    {
        Success = success;
        Message = message;
        Errors = errors ?? [];
    }

    public bool Success { get; }

    public string Message { get; }

    public IReadOnlyList<string> Errors { get; }

    public static OperationResult Ok(string message = "Operation completed.") => new(true, message);

    public static OperationResult Fail(string message, params string[] errors) => new(false, message, errors);
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool success, string message, T? value, IReadOnlyList<string>? errors = null)
        : base(success, message, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Ok(T value, string message = "Operation completed.")
        => new(true, message, value);

    public new static OperationResult<T> Fail(string message, params string[] errors)
        => new(false, message, default, errors);
}
