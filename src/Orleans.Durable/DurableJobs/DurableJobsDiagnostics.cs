using System;
using System.Collections.Generic;
using System.Diagnostics;
using Orleans.Diagnostics;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 帮助创造持久就业的分布式跟踪活动。
/// </summary>
internal static class DurableJobsDiagnostics
{
    /// <summary>
    /// 用于持久工作的活动来源跨度。
    /// </summary>
    public static ActivitySource Source { get; } = new(ActivitySources.DurableJobsActivitySourceName, "1.0.0");

    public const string ActivityScheduleJob = "schedule durable job";
    public const string ActivityExecuteJob = "execute durable job";
    public const string ActivityExecuteJobHandler = "execute durable job handler";
    public const string ActivityPersistShardBatch = "persist durable job shard batch";
    public const string ActivityDeleteShard = "delete durable job shard";

    private const int MaxBatchLinks = 128;

    /// <summary>
    /// 返回所提供活动的 W3C <c>traceparent</c>/<c>tracestate</c> ,或者当没有 W3C活动可用时,返回两个 <see langword="null"/> 。
    /// </summary>
    public static (string? TraceParent, string? TraceState) CaptureTraceContext(Activity? activity)
    {
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C || activity.Id is null)
        {
            return (null, null);
        }

        return (activity.Id, activity.TraceStateString);
    }

    /// <summary>
    /// 尝试将一个长期存在的 W3C 跟踪上下文解析成 <see cref="ActivityContext"/>。
    /// </summary>
    public static bool TryParseTraceContext(string? traceParent, string? traceState, out ActivityContext context)
    {
        if (string.IsNullOrEmpty(traceParent))
        {
            context = default;
            return false;
        }

        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out context);
    }

    /// <summary>
    /// 启动生产者活动,以申请日程安排,并在分配工作标识符后标有工作属性。
    /// </summary>
    public static Activity? StartScheduleActivity(in ScheduleJobRequest request)
    {
        var activity = Source.StartActivity(ActivityScheduleJob, ActivityKind.Producer);
        if (activity is not null)
        {
            activity.SetTag(ActivityTagKeys.DurableJobName, request.JobName);
            activity.SetTag(ActivityTagKeys.DurableJobTargetGrainId, request.Target.ToString());
            activity.SetTag(ActivityTagKeys.DurableJobDueTime, request.DueTime.ToString("O"));
        }

        return activity;
    }

    /// <summary>
    /// 将长期存在/分配的工作身份标记附在日程安排活动之后。
    /// </summary>
    public static void SetScheduledJobTags(Activity? activity, DurableJob job)
    {
        if (activity is null || job is null)
        {
            return;
        }

        activity.SetTag(ActivityTagKeys.DurableJobId, job.Id);
        activity.SetTag(ActivityTagKeys.DurableJobShardId, job.ShardId);
    }

    /// <summary>
    /// 启动一项消费者活动,以开展一项持久的工作,可选择继续跟踪在安排该工作时记录的情况。
    /// </summary>
    public static Activity? StartExecuteActivity(DurableJob job, int dequeueCount, string? runId)
    {
        ArgumentNullException.ThrowIfNull(job);

        Activity? activity;
        if (TryParseTraceContext(job.TraceParent, job.TraceState, out var parent))
        {
            activity = Source.StartActivity(ActivityExecuteJob, ActivityKind.Consumer, parent);
        }
        else
        {
            activity = Source.StartActivity(ActivityExecuteJob, ActivityKind.Consumer);
        }

        if (activity is not null)
        {
            activity.SetTag(ActivityTagKeys.DurableJobId, job.Id);
            activity.SetTag(ActivityTagKeys.DurableJobName, job.Name);
            activity.SetTag(ActivityTagKeys.DurableJobShardId, job.ShardId);
            activity.SetTag(ActivityTagKeys.DurableJobTargetGrainId, job.TargetGrainId.ToString());
            activity.SetTag(ActivityTagKeys.DurableJobDueTime, job.DueTime.ToString("O"));
            activity.SetTag(ActivityTagKeys.DurableJobDequeueCount, dequeueCount);
            if (runId is not null)
            {
                activity.SetTag(ActivityTagKeys.DurableJobRunId, runId);
            }
        }

        return activity;
    }

    /// <summary>
    /// 启动一项内部活动,涵盖执行职务时处理人的调用部分。
    /// </summary>
    public static Activity? StartHandlerActivity(DurableJob job, int dequeueCount, string? runId)
    {
        ArgumentNullException.ThrowIfNull(job);

        var activity = Source.StartActivity(ActivityExecuteJobHandler, ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag(ActivityTagKeys.DurableJobId, job.Id);
            activity.SetTag(ActivityTagKeys.DurableJobName, job.Name);
            activity.SetTag(ActivityTagKeys.DurableJobShardId, job.ShardId);
            activity.SetTag(ActivityTagKeys.DurableJobDequeueCount, dequeueCount);
            if (runId is not null)
            {
                activity.SetTag(ActivityTagKeys.DurableJobRunId, runId);
            }
        }

        return activity;
    }

    /// <summary>
    /// 记录使用 OpenTelemetry 语义-公约标记的活动的例外。
    /// </summary>
    public static void SetError(Activity? activity, Exception exception)
    {
        if (activity is null || exception is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        if (activity.IsAllDataRequested)
        {
            activity.SetTag(ActivityTagKeys.ExceptionType, exception.GetType().FullName);
            activity.SetTag(ActivityTagKeys.ExceptionMessage, exception.Message);
            activity.SetTag(ActivityTagKeys.ExceptionStacktrace, exception.ToString());
            activity.SetTag(ActivityTagKeys.ExceptionEscaped, true);
        }
    }

    /// <summary>
    /// 启动内部活动,将分批的持久性包装成一个工作碎片。第一个非默认
    /// 后被捕获的上下文成为父;随后不同的上下文以 <see cref="ActivityLink"/>s记录
    /// 以便从每个捐款时间表的追踪中 达到相应的范围
    /// </summary>
    /// <typeparam name="T">待定操作类型( 必须暴露被俘的 <see cref="ActivityContext"/>).</typeparam>
    public static Activity? StartPersistBatchActivity<T>(IReadOnlyList<T> operations, string shardId)
        where T : class
    {
        ActivityContext parent = default;
        List<ActivityLink>? links = null;

        if (operations is not null)
        {
            for (var i = 0; i < operations.Count; i++)
            {
                var context = GetCapturedContext(operations[i]);
                if (context == default)
                {
                    continue;
                }

                if (parent == default)
                {
                    parent = context;
                    continue;
                }

                if (context == parent)
                {
                    continue;
                }

                links ??= new List<ActivityLink>();
                if (links.Count >= MaxBatchLinks)
                {
                    continue;
                }

                var duplicate = false;
                for (var j = 0; j < links.Count; j++)
                {
                    if (links[j].Context == context)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    links.Add(new ActivityLink(context));
                }
            }
        }

        var activity = Source.StartActivity(ActivityPersistShardBatch, ActivityKind.Internal, parent, tags: null, links: links);
        if (activity is not null)
        {
            activity.SetTag(ActivityTagKeys.DurableJobShardId, shardId);
            activity.SetTag(ActivityTagKeys.JournalStorageOperation, "WriteState");
            activity.SetTag(ActivityTagKeys.JournalStorageBatchSize, operations?.Count ?? 0);
        }

        return activity;
    }

    /// <summary>
    /// 启动一个内部活动,以包住“硬状态”删除。使用后产生的存储间隔是
    /// 与原删除请求的相同。
    /// </summary>
    public static Activity? StartDeleteShardActivity(ActivityContext parent, string shardId)
    {
        var activity = parent == default
            ? Source.StartActivity(ActivityDeleteShard, ActivityKind.Internal)
            : Source.StartActivity(ActivityDeleteShard, ActivityKind.Internal, parent);
        if (activity is not null)
        {
            activity.SetTag(ActivityTagKeys.DurableJobShardId, shardId);
            activity.SetTag(ActivityTagKeys.JournalStorageOperation, "DeleteState");
        }

        return activity;
    }

    private static ActivityContext GetCapturedContext(object operation)
    {
        // PendingOperation 生活在内心 JournaledJobShard向公众进行宣传 CapturedContext 以无反射鸭形形式
        // 使用专用界面将辅助器与私人类型脱钩。
        if (operation is IHasCapturedContext capturing)
        {
            return capturing.CapturedContext;
        }

        return default;
    }

    /// <summary>
    /// 使用在后排时间捕捉到的 <see cref="ActivityContext"/> 类型执行,以便存储能够
    /// 后作为原活动(或与原活动有关)的子孙而创建。
    /// </summary>
    public interface IHasCapturedContext
    {
        ActivityContext CapturedContext { get; }
    }
}
