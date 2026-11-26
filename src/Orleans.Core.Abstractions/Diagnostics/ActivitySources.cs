using System.Diagnostics;

namespace Orleans.Diagnostics;

/// <summary>
/// 为 Orleans <see cref="ActivitySource"/> 实例提供名称。
/// </summary>
public static class ActivitySources
{
    /// <summary>
    /// 程序级别代码触发的 Spans
    /// </summary>
    public const string ApplicationGrainActivitySourceName = "Microsoft.Orleans.Application";
    /// <summary>
    /// 由 Orleans 运行时代码触发的 Spaans
    /// </summary>
    public const string RuntimeActivitySourceName = "Microsoft.Orleans.Runtime";
    /// <summary>
    /// 与激活、迁移和解除作用等生命周期操作有关的垃圾邮件。
    /// </summary>
    public const string LifecycleActivitySourceName = "Microsoft.Orleans.Lifecycle";
    /// <summary>
    /// 与持久性储存作业有关的垃圾邮件。
    /// </summary>
    public const string StorageActivitySourceName = "Microsoft.Orleans.Storage";
    /// <summary>
    /// 与持久工作时间安排和执行挂钩。
    /// </summary>
    public const string DurableJobsActivitySourceName = "Microsoft.Orleans.DurableJobs";
    /// <summary>
    /// 匹配全部 Orleans 活动源的通配符名称。
    /// </summary>
    public const string AllActivitySourceName = "Microsoft.Orleans.*";

    internal static readonly ActivitySource ApplicationGrainSource = new(ApplicationGrainActivitySourceName, "1.2.0");
    internal static readonly ActivitySource RuntimeGrainSource = new(RuntimeActivitySourceName, "2.1.0");
    internal static readonly ActivitySource LifecycleGrainSource = new(LifecycleActivitySourceName, "1.1.0");
    internal static readonly ActivitySource StorageGrainSource = new(StorageActivitySourceName, "1.1.0");
    internal static readonly ActivitySource DurableJobsSource = new(DurableJobsActivitySourceName, "1.0.0");
}
