using System;
using System.Collections.Generic;
using System.Diagnostics;
using Orleans.Diagnostics;

namespace Orleans.Durable.Jobs;

/// <summary>
/// 用于创建持久化作业分布式跟踪 Activity 的辅助方法。
/// </summary>
internal static class DurableJobsDiagnostics
{
    /// <summary>
    /// 持久化作业跟踪跨度使用的 ActivitySource。
    /// </summary>
    public static ActivitySource Source { get; } = new(ActivitySources.DurableJobsActivitySourceName, "1.0.0");

    public const string ActivityScheduleJob = "schedule durable job";
    public const string ActivityExecuteJob = "execute durable job";
    public const string ActivityExecuteJobHandler = "execute durable job handler";
    public const string ActivityPersistShardBatch = "persist durable job shard batch";
    public const string ActivityDeleteShard = "delete durable job shard";

    private const int MaxBatchLinks = 128;

    /// <summary>
    /// 返回指定 Activity 的 W3C <c>traceparent</c>/<c>tracestate</c> 值；如果没有可用的 W3C Activity，则两者均为 <see langword="null"/>。
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
    /// 尝试将已持久化的 W3C 跟踪上下文解析为 <see cref="ActivityContext"/>。
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
    /// 为调度请求启动 Producer Activity，并在分配作业 ID 后添加作业属性标签。
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
    /// 将已持久化或已分配的作业标识标签附加到调度 Activity。
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
    /// 为持久化作业的执行启动 Consumer Activity，并可选择延续调度作业时记录的跟踪。
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
    /// 启动一个覆盖作业执行中处理程序调用阶段的内部 Activity。
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
    /// 使用 OpenTelemetry 语义约定标签将异常记录到 Activity。
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
    /// 启动一个封装作业分片批量持久化写入的内部 Activity。第一个非默认的
    /// 捕获上下文作为父上下文；后续不同的上下文记录为 <see cref="ActivityLink"/>，
    /// 使每个参与该批次的调度跟踪都能够关联到生成的跟踪跨度。
    /// </summary>
    /// <typeparam name="T">待处理操作的类型（必须公开捕获的 <see cref="ActivityContext"/>）。</typeparam>
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
    /// 启动一个封装分片状态删除操作的内部 Activity，使生成的存储跟踪跨度属于
    /// 发起删除请求的同一条跟踪链路。
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
        // PendingOperation 位于 JournaledJobShard 内部。通过专用接口以无需反射的鸭子类型方式访问公开的 CapturedContext 属性，
        // 从而使该辅助方法与私有类型解耦。
        if (operation is IHasCapturedContext capturing)
        {
            return capturing.CapturedContext;
        }

        return default;
    }

    /// <summary>
    /// 由携带入队时捕获的 <see cref="ActivityContext"/> 的类型实现，以便稍后创建存储跟踪跨度，
    /// 作为原始 Activity 的子跨度或通过链接关联到原始 Activity。
    /// </summary>
    public interface IHasCapturedContext
    {
        ActivityContext CapturedContext { get; }
    }
}
