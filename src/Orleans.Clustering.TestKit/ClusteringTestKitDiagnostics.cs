namespace Orleans.Clustering.TestKit;

/// <summary>无条件违反成员资格合同,不以任何测试框架为限。</summary>
public sealed class ClusteringConformanceException : Exception
{
    /// <summary>产生符合性故障,并附有可操作的观察细节。</summary>
    public ClusteringConformanceException(string message) : base(message) { }

    /// <summary>创建符合性故障,保留基本操作故障。</summary>
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
