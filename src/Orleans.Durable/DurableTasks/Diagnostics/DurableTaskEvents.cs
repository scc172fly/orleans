using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks.Diagnostics;

/// <summary>
/// 提供 Grain 持久化任务运行时的诊断侦听器及事件有效载荷类型。
/// </summary>
/// <remarks>
/// <para>
/// 这些类型是公开的，但可能在次要版本之间发生变化。它们适用于
/// 模拟测试和诊断等高级场景。
/// </para>
/// <para>
/// 每个事件在运行时暂存状态转换时，由发生该转换的激活实例同步写入，
/// 因此观察者能够看到运行时在该时刻的状态；该变更随 Grain 的下一次日志
/// 写入提交。观察者抛出的异常会传播到引发该事件的运行时操作。
/// </para>
/// </remarks>
public static class DurableTaskEvents
{
    /// <summary>持久化任务运行时事件的诊断侦听器名称。</summary>
    public const string ListenerName = "Orleans.DurableTasks";

    private static readonly DiagnosticListener Listener = new(ListenerName);

    /// <summary>获取包含所有持久化任务运行时事件的可观察序列。</summary>
    public static IObservable<DurableTaskEvent> AllEvents { get; } = new Observable();

    internal static bool IsTaskCreatedEnabled => Listener.IsEnabled(nameof(TaskCreated));

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Event payloads are sealed types whose public fields and properties the generic annotation preserves, and subscribers consume the typed payload rather than reflecting over the members of the values it carries.")]
    private static void Write<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TEvent>(string name, TEvent payload)
        where TEvent : DurableTaskEvent
        => Listener.Write(name, payload);

    /// <summary>持久化任务运行时诊断事件的基类。</summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">事件所描述的任务。</param>
    public abstract class DurableTaskEvent(IGrainContext grainContext, TaskId taskId)
    {
        /// <summary>其运行时引发该事件的激活实例。</summary>
        public readonly IGrainContext GrainContext = grainContext;

        /// <summary>事件所描述的任务。</summary>
        public readonly TaskId TaskId = taskId;
    }

    /// <summary>运行时暂存其状态的任务的事件有效载荷：该任务可以是被调用的任务，也可以是执行过程中的操作。</summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">已创建的任务。</param>
    public sealed class TaskCreated(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>运行时暂存任务终态结果时的事件有效载荷。</summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">已完成的任务。</param>
    /// <param name="response">任务的终态结果。</param>
    public sealed class TaskCompleted(IGrainContext grainContext, TaskId taskId, DurableTaskResponse response)
        : DurableTaskEvent(grainContext, taskId)
    {
        /// <summary>任务的终态结果。</summary>
        public readonly DurableTaskResponse Response = response;
    }

    /// <summary>
    /// 运行时暂存任务删除时的事件有效载荷：任务随其执行树删除，或在保留期结束时删除。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">已删除的任务。</param>
    public sealed class TaskDeleted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>运行时在激活实例上启动任务主体的一次执行尝试时的事件有效载荷。</summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">正在运行其主体的任务。</param>
    public sealed class ExecutionStarted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 任务主体在激活实例上的一次执行尝试结束时的事件有效载荷：任务主体已完成，或运行时
    /// 放弃了此次尝试，例如任务因中止或删除而结束。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">已运行其主体的任务。</param>
    public sealed class ExecutionStopped(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>执行开始等待尚未到达的信号时的事件有效载荷。</summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">接收信号值的记录。</param>
    public sealed class SignalWaitStarted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 执行结束信号等待时的事件有效载荷：信号值已提交，或者等待已
    /// 取消。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">接收信号值的记录。</param>
    public sealed class SignalWaitEnded(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 选择最先完成的任务（例如 <c>DurableTask.WhenAny</c>）时的事件有效载荷，此时尚无
    /// 已完成的候选任务，因而需要等待其中一个完成。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">选择操作的决策记录，其中记录了胜出的任务。</param>
    public sealed class SelectionWaitStarted(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 任务主体的一次尝试已重放先前各次尝试记录的操作，并
    /// 开始执行此前未记录的操作时的事件有效载荷：从此时起，<c>DurableTaskContext.IsReplaying</c> 返回
    /// <see langword="false"/>。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">正在运行其主体的任务。</param>
    public sealed class ReplayEnded(IGrainContext grainContext, TaskId taskId) : DurableTaskEvent(grainContext, taskId)
    {
    }

    /// <summary>
    /// 执行记录版本决策时的事件有效载荷：该决策记录此执行从此采用的变更
    /// 版本，<c>DurableTask.GetVersion</c> 会向此执行及其步骤返回该版本。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">记录该决策的执行。</param>
    /// <param name="name">变更名称。</param>
    /// <param name="version">
    /// 记录的版本：首次到达该调用的执行采用调用代码的最新版本；若某次执行在更早的部署中已经越过该调用，
    /// 则为 <c>0</c>。
    /// </param>
    public sealed class VersionRecorded(IGrainContext grainContext, TaskId taskId, string name, int version)
        : DurableTaskEvent(grainContext, taskId)
    {
        /// <summary>变更的名称。</summary>
        public readonly string Name = name;

        /// <summary>记录的版本。</summary>
        public readonly int Version = version;
    }

    /// <summary>
    /// 执行尝试因当前运行代码无法重放该执行而停滞时的事件有效载荷：执行
    /// 记录了代码不支持的版本，或者代码与已记录的执行发生分歧。运行时
    /// 丢弃此次尝试的结果，使任务保持待处理状态，并由之后的激活实例重放。
    /// </summary>
    /// <param name="grainContext">其运行时引发该事件的激活实例。</param>
    /// <param name="taskId">停滞的执行实例。</param>
    /// <param name="reason">
    /// 尝试停滞的原因：<see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 或
    /// <see cref="DurableTaskAttemptCancellationReason.Diverged"/>.
    /// </param>
    /// <param name="exception">
    /// 描述停滞原因的异常：对于不支持的版本为 <see cref="DurableTaskVersionException"/>，或者
    /// 为描述重放分歧的 <see cref="InvalidOperationException"/>。
    /// </param>
    public sealed class ExecutionStalled(IGrainContext grainContext, TaskId taskId, DurableTaskAttemptCancellationReason reason, Exception exception)
        : DurableTaskEvent(grainContext, taskId)
    {
        /// <summary>尝试停滞的原因。</summary>
        public readonly DurableTaskAttemptCancellationReason Reason = reason;

        /// <summary>描述停滞原因的异常。</summary>
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
