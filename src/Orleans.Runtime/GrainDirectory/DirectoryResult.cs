using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime;

internal static class DirectoryResult
{
    public static DirectoryResult<T> FromResult<T>(T result, MembershipVersion version) => new(result, version);
    public static DirectoryResult<T> RefreshRequired<T>(MembershipVersion version) => new(default, version);
    public static DirectoryResult<T> RetryAfter<T>(TimeSpan retryAfter) => new(retryAfter);
}

[GenerateSerializer, Alias("DirectoryResult`1"), Immutable]
internal readonly struct DirectoryResult<T>
{
    [Id(0)]
    private readonly T? _result;

    [Id(1)]
    public readonly MembershipVersion Version;

    /// <summary>
    /// 当大于 <see cref="TimeSpan.Zero"/> 时，表示调用方应在指定延迟之后重试。
    /// </summary>
    [Id(2)]
    public readonly TimeSpan RetryAfterDelay;

    public DirectoryResult(T? result, MembershipVersion version)
    {
        _result = result;
        Version = version;
    }

    public DirectoryResult(TimeSpan retryAfter)
    {
        RetryAfterDelay = retryAfter;
    }

    public bool TryGetResult(MembershipVersion version, [NotNullWhen(true)] out T? result)
    {
        if (RetryAfterDelay <= TimeSpan.Zero && Version == version)
        {
            result = _result!;
            return true;
        }

        result = default;
        return false;
    }
}
