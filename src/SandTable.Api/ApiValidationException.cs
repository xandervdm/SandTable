namespace SandTable.Api;

public sealed class ApiValidationException(
    string title,
    string detail,
    IReadOnlyDictionary<string, string[]>? errors = null) : InvalidOperationException(detail)
{
    public string Title { get; } = title;

    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
}
