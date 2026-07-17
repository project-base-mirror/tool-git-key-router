namespace GitKeyRouter.Core.Validation;

public sealed class ValidationResult
{
    private readonly List<string> _errors = [];

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyList<string> Errors => _errors;

    public void Add(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            _errors.Add(error);
        }
    }
}
