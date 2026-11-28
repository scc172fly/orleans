using System.Diagnostics;

namespace Orleans.Diagnostics;

/// <summary>
/// 提供 Orleans <see cref="ActivitySource"/> 实例的名称。
/// </summary>
public static class ActivitySources
{
    /// <summary>
    /// 应用程序级代码触发的跟踪跨度（Span）。
    /// </summary>
    public const string ApplicationGrainActivitySourceName = "Microsoft.Orleans.Application";
    /// <summary>
    /// Orleans 运行时代码触发的跟踪跨度。
    /// </summary>
    public const string RuntimeActivitySourceName = "Microsoft.Orleans.Runtime";
    /// <summary>
    /// 与激活、迁移和停用等生命周期操作关联的跟踪跨度。
    /// </summary>
    public const string LifecycleActivitySourceName = "Microsoft.Orleans.Lifecycle";
    /// <summary>
    /// 与持久化存储操作关联的跟踪跨度。
    /// </summary>
    public const string StorageActivitySourceName = "Microsoft.Orleans.Storage";
    /// <summary>
    /// 与持久化作业调度及执行关联的跟踪跨度。
    /// </summary>
    public const string DurableJobsActivitySourceName = "Microsoft.Orleans.DurableJobs";
    /// <summary>
    /// 用于匹配所有 Orleans 活动源的通配符名称。
    /// </summary>
    public const string AllActivitySourceName = "Microsoft.Orleans.*";

    internal static readonly ActivitySource ApplicationGrainSource = new(ApplicationGrainActivitySourceName, "1.2.0");
    internal static readonly ActivitySource RuntimeGrainSource = new(RuntimeActivitySourceName, "2.1.0");
    internal static readonly ActivitySource LifecycleGrainSource = new(LifecycleActivitySourceName, "1.1.0");
    internal static readonly ActivitySource StorageGrainSource = new(StorageActivitySourceName, "1.1.0");
    internal static readonly ActivitySource DurableJobsSource = new(DurableJobsActivitySourceName, "1.0.0");
}
