using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Diagnostics;

/// <summary>
/// 提供 grains的持久任务运行时间诊断听筒和事件有效载荷类型。
/// </summary>
/// <remarks>
/// <para>
/// 这些类型是公开的,但可能在不同版本之间发生变化。
/// 模拟测试和诊断。
/// </para>
/// <para>
/// 启动时运行时间导致过渡的激活,当启动阶段
/// 转换承诺于 grain的下一期日记
/// 写作。观察者提出的例外,是宣传引起事件运行的运行时间操作。
/// </para>
/// </remarks>
public static class DurableTaskEvents
{
    /// <summary>耐久任务运行时间事件诊断倾听器的名称。</summary>
    public const string ListenerName = "Orleans.DurableTasks";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>获得所有持久任务运行时间事件的可观察序列。</summary>
    public static IObservable<DurableTaskEvent> AllEvents { get; } = new Observable();

    internal static bool IsTaskCreatedEnabled => Listener.IsEnabled(nameof(TaskCreated));

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Event payloads are sealed types whose public fields and properties the generic annotation preserves, and subscribers consume the typed payload rather than reflecting over the members of the values it carries.")]
    private static void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TEvent>(string name, TEvent payload)
        where TEvent : DurableTaskEvent
        => Listener.Write(name, payload);

    /// <summary>用于耐久任务运行时间诊断事件的基准类。</summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">事件描述的任务。</param>
    public abstract class DurableTaskEvent(IGrainContext grainContext, TaskId taskId)
    {
        /// <summary>启动时间的运行时间引发了事件。</summary>
        public readonly IGrainContext GrainContext = grainContext;

        /// <summary>事件描述的任务。</summary>
        public readonly TaskId TaskId = taskId;
    }

    /// <summary>一项任务,即运行时间已经确定的任务:援引的任务或执行的操作,其事件有效载荷。</summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">创建的任务。</param>
    public sealed class TaskCreated(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>任务最终结果为运行时间已定的运行时,该任务的事件有效载荷。</summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">完成的任务。</param>
    /// <param name="response">任务终端结果。</param>
    public sealed class TaskCompleted(IGrainContext grainContext, TaskId taskId, DurableTaskResponse response)
        : DurableTaskEvent(grainContext, taskId)
    {
        /// <summary>任务终端结果。</summary>
        public readonly DurableTaskResponse Response = response;
    }

    /// <summary>
    /// 用于一项任务,即删除运行时间已逐步确定、带有执行树或保留结束的任务的事件发生有效载荷。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">删除的任务。</param>
    public sealed class TaskDeleted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>任务体尝试时的事件有效载荷,启动时运行时间即开始。</summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">其正文运行的任务。</param>
    public sealed class ExecutionStarted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 任务体在激活时结束的尝试中的事件有效载荷:完成身体或运行时间
    /// 放弃尝试,例如因为中止或删除终止了任务。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">机构运行的任务。</param>
    public sealed class ExecutionStopped(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>执行中开始等待尚未到达的信号的事件有效载荷。</summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">接收信号值的记录。</param>
    public sealed class SignalWaitStarted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 执行执行时等待结束的信号的事件有效载荷: 承诺执行的信号值, 或者等待
    /// 已取消 。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">接收信号值的记录。</param>
    public sealed class SignalWaitEnded(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 选择要完成的第一个任务, 例如 <c>DurableTask.WhenAny</c>的, 发现没有
    /// 候选人完成,等待一位。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">选择中选的决定记录了胜者。</param>
    public sealed class SelectionWaitStarted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 重播先前所记录操作的重播任务机构尝试中的 事件有效载荷
    /// 开始一项操作,但先前没有记录任何尝试: <c>DurableTaskContext.IsReplaying</c> 报告
    /// 从此<see langword="false"/> 。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">其正文运行的任务。</param>
    public sealed class ReplayEnded(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 执行记录到的版本决定事件有效载荷:
    /// 随后, <c>DurableTask.GetVersion</c> 返回到它及其步骤。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">执行决定的记录。</param>
    /// <param name="name">更改的名称。</param>
    /// <param name="version">
    /// 录音版本:呼叫代码的最新版本 执行的,第一个到达了通话,或者
    /// 先前部署的弹药经过时,其<c>0</c> 。
    /// </param>
    public sealed class VersionRecorded(IGrainContext grainContext, TaskId taskId, string name, int version)
        : DurableTaskEvent(grainContext, taskId)
    {
        /// <summary>更改的名称。</summary>
        public readonly string Name = name;

        /// <summary>录音版本。</summary>
        public readonly int Version = version;
    }

    /// <summary>
    /// 运行代码无法重弹执行, 导致事件有效载荷停止:
    /// 记录了代码不支持的版本, 或者代码与所记录的执行有差异。 运行时间
    /// 放弃尝试的结果, 并保持任务待定, 并且稍后激活重播它。
    /// </summary>
    /// <param name="grainContext">启动时间的运行时间引发了事件。</param>
    /// <param name="taskId">死刑的停顿</param>
    /// <param name="reason">
    /// 为何企图停滞不前: <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 或
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>.
    /// </param>
    /// <param name="exception">
    /// 描述档段的例外:一个不支持版本的 <see cref="DurableTaskVersionException"/> ,或
    /// a <see cref="InvalidOperationException"/> ,其中说明差异。
    /// </param>
    public sealed class ExecutionStalled(IGrainContext grainContext, TaskId taskId, DurableTaskAttemptCancellationReason reason, Exception exception)
        : DurableTaskEvent(grainContext, taskId)
    {
        /// <summary>为什么尝试停滞了</summary>
        public readonly DurableTaskAttemptCancellationReason Reason = reason;

        /// <summary>描述摊位的例外。</summary>
        public readonly Exception Exception = exception;
    }

    internal static void EmitTaskCreated(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(TaskCreated)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(TaskCreated), new TaskCreated(grainContext, taskId));
    }

    internal static void EmitTaskCompleted(IGrainContext grainContext, TaskId taskId, DurableTaskResponse response)
    {
        if (!Listener.IsEnabled(nameof(TaskCompleted)))
        {
            return;
        }

        Emit(grainContext, taskId, response);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId, DurableTaskResponse response) =>
            Write(nameof(TaskCompleted), new TaskCompleted(grainContext, taskId, response));
    }

    internal static void EmitTaskDeleted(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(TaskDeleted)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(TaskDeleted), new TaskDeleted(grainContext, taskId));
    }

    internal static void EmitExecutionStarted(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(ExecutionStarted)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(ExecutionStarted), new ExecutionStarted(grainContext, taskId));
    }

    internal static void EmitExecutionStopped(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(ExecutionStopped)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(ExecutionStopped), new ExecutionStopped(grainContext, taskId));
    }

    internal static void EmitSignalWaitStarted(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(SignalWaitStarted)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(SignalWaitStarted), new SignalWaitStarted(grainContext, taskId));
    }

    internal static void EmitSignalWaitEnded(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(SignalWaitEnded)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(SignalWaitEnded), new SignalWaitEnded(grainContext, taskId));
    }

    internal static void EmitSelectionWaitStarted(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(SelectionWaitStarted)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(SelectionWaitStarted), new SelectionWaitStarted(grainContext, taskId));
    }

    internal static void EmitReplayEnded(IGrainContext grainContext, TaskId taskId)
    {
        if (!Listener.IsEnabled(nameof(ReplayEnded)))
        {
            return;
        }

        Emit(grainContext, taskId);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId) =>
            Write(nameof(ReplayEnded), new ReplayEnded(grainContext, taskId));
    }

    internal static void EmitVersionRecorded(IGrainContext grainContext, TaskId taskId, string name, int version)
    {
        if (!Listener.IsEnabled(nameof(VersionRecorded)))
        {
            return;
        }

        Emit(grainContext, taskId, name, version);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId, string name, int version) =>
            Write(nameof(VersionRecorded), new VersionRecorded(grainContext, taskId, name, version));
    }

    internal static void EmitExecutionStalled(IGrainContext grainContext, TaskId taskId, DurableTaskAttemptCancellationReason reason, Exception exception)
    {
        if (!Listener.IsEnabled(nameof(ExecutionStalled)))
        {
            return;
        }

        Emit(grainContext, taskId, reason, exception);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Emit(IGrainContext grainContext, TaskId taskId, DurableTaskAttemptCancellationReason reason, Exception exception) =>
            Write(nameof(ExecutionStalled), new ExecutionStalled(grainContext, taskId, reason, exception));
    }

    private sealed class Observable : IObservable<DurableTaskEvent>
    {
        public IDisposable Subscribe(IObserver<DurableTaskEvent> observer) => Listener.Subscribe(new Observer(observer));

        private sealed class Observer(IObserver<DurableTaskEvent> observer) : IObserver<KeyValuePair<string, object?>>
        {
            public void OnCompleted() => observer.OnCompleted();

            public void OnError(Exception error) => observer.OnError(error);

            public void OnNext(KeyValuePair<string, object?> value)
            {
                if (value.Value is DurableTaskEvent evt)
                {
                    observer.OnNext(evt);
                }
            }
        }
    }
}
