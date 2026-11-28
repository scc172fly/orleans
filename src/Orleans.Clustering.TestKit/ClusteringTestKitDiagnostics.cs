namespace Orleans.Clustering.TestKit;

/// <summary>不依赖任何测试框架的无条件成员资格契约违规。</summary>
public sealed class ClusteringConformanceException : Exception
{
    /// <summary>创建包含可用于排查问题的观测详情的符合性测试失败。</summary>
    public ClusteringConformanceException(string message) : base(message) { }

    /// <summary>创建保留底层操作失败原因的符合性测试失败。</summary>
    public ClusteringConformanceException(string message, Exception innerException) : base(message, innerException) { }
}

internal static class ClusteringTestKitDiagnostics
{
    internal const string CleanupFailureKey = "ClusteringTestKit.CleanupFailure";
    internal const string CleanupCompletionKey = "ClusteringTestKit.CleanupCompletion";

    internal static ClusteringConformanceException CreateFailure(
        string provider, string guarantee, string cluster, string handle, int seed, string detail, Exception? innerException = null)
    {
        var message = $"Membership conformance: provider={provider}; guarantee={guarantee}; cluster={cluster}; handle={handle}; seed={seed}; {detail}";
        return innerException is null ? new(message) : new(message, innerException);
    }

    internal static string FormatHistory(int seed, int caseNumber, IEnumerable<string> prefix, string detail)
        => $"seed={seed}; case={caseNumber}; prefix=[{string.Join(" -> ", prefix)}]; {detail}";

    internal static void AttachCleanupFailure(Exception primary, Exception cleanup)
        => primary.Data[CleanupFailureKey] = cleanup;

    internal static void Require(bool condition, string detail)
    {
        if (!condition)
        {
            throw new ClusteringConformanceException(detail);
        }
    }
}
