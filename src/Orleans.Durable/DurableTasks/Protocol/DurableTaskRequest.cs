using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Orleans.CodeGeneration;
using Orleans.Durable.Tasks;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Invocation;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Durable.Tasks.Protocol;

/// <summary>
/// 表示持久化任务请求。
/// </summary>
public interface IDurableTaskRequest : IRequest
{
    /// <summary>
    /// 获取任务请求上下文。
    /// </summary>
    DurableTaskRequestContext? Context { get; }

    /// <summary>
    /// 在目标上调用方法。
    /// </summary>
    /// <returns>调用结果。</returns>
    //ValueTask<DurableTaskResponse> InvokeImplementation(DurableTaskContext executionContext);
    DurableTask CreateTask();

    /// <summary>
    /// 返回请求的字符串表示形式。
    /// </summary>
    /// <returns>请求的字符串表示形式。</returns>
    public string ToMethodCallString() => ToMethodCallString(this);

    /// <summary>
    /// 返回 <paramref name="request"/> 的上下文，由创建请求的 Grain 引用设置。
    /// </summary>
    /// <exception cref="InvalidOperationException">该请求没有上下文。</exception>
    internal static DurableTaskRequestContext GetContext(IDurableTaskRequest request) =>
        request.Context
        ?? throw new InvalidOperationException(
            $"Durable task request '{request.GetType()}' has no context. A grain reference creates durable task requests with their context.");

    /// <summary>
    /// 返回 <paramref name="request"/> 所调用接口方法的标识：其可调用对象的格式化类型，
    /// 代码生成器为每个接口方法生成一个这样的类型。
    /// </summary>
    /// <remarks>
    /// 重放操作及按其标识符重试根任务时，如果它们在相同目标上调用
    /// 相同的方法，则属于同一操作。标识不包含参数，因此改变
    /// 参数序列化方式的部署（例如添加可选字段）仍可重放相同的操作。
    /// </remarks>
    internal static string GetMethod(IDurableTaskRequest request) => RuntimeTypeNameFormatter.Format(request.GetType());

    /// <summary>
    /// 返回 <paramref name="request"/> 的可调用对象类型及序列化参数的哈希，由诊断信息报告。
    /// </summary>
    /// <remarks>
    /// 只要参数的序列化形式变化，哈希就会变化，因此它从不用于判断两个请求
    /// 是否属于同一操作：由 <see cref="GetMethod"/> 判断。
    /// </remarks>
    internal static string GetFingerprint(IDurableTaskRequest request, Serializer serializer)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(Encoding.UTF8.GetBytes(RuntimeTypeNameFormatter.Format(request.GetType())));

        var arguments = new object?[request.GetArgumentCount()];
        for (var index = 0; index < request.GetArgumentCount(); index++)
        {
            arguments[index] = request.GetArgument(index);
        }

        using var stream = new MemoryStream();
        using var session = serializer.SessionPool.GetSession();
        serializer.Serialize(arguments, stream, session);
        Append(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
        return Convert.ToHexString(hash.GetHashAndReset());

        void Append(ReadOnlySpan<byte> value)
        {
            Span<byte> length = stackalloc byte[sizeof(int)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }
    }
}

public sealed class DurableTaskRequestShared(IGrainContextAccessor grainContextAccessor, IGrainFactory grainFactory)
{
    public IGrainContextAccessor GrainContextAccessor { get; } = grainContextAccessor;
    public IGrainFactory GrainFactory { get; } = grainFactory;
}

[GenerateSerializer]
[ReturnValueProxy(initializerMethodName: nameof(InitializeRequest))]
[Alias("DurableTaskRequest")]
[method: GeneratedActivatorConstructor]
public abstract class DurableTaskRequest(DurableTaskRequestShared shared) : DurableTask, IDurableTaskRequest, ISchedulableTask
{
    // 注意：这里可以使用 RuntimeContext 来省去一个字段，但这样需要使内部成员对本程序集可见。
    // 暂时不这样做，以确认不依赖它也能完成大部分工作，从而展示 Orleans 的可扩展性。
    // 即使不推荐这种方式，将来某个时候也可能值得把 RuntimeContext 公开。
    [NonSerialized]
    private readonly DurableTaskRequestShared _shared = shared;

    /// <inheritdoc />
    [Id(0)]
    public DurableTaskRequestContext? Context { get; private set; }

    /// <summary>
    /// 获取调用选项。
    /// </summary>
    [field: NonSerialized]
    public InvokeMethodOptions Options { get; private set; }

    /// <inheritdoc/>
    public virtual int GetArgumentCount() => 0;

    /// <summary>
    /// 合并提供的调用选项。
    /// </summary>
    /// <param name="options">
    /// 选项。
    /// </param>
    public void AddInvokeMethodOptions(InvokeMethodOptions options) => Options |= options;

    /// <inheritdoc/>
    public abstract object GetTarget();

    /// <inheritdoc/>
    public abstract void SetTarget(ITargetHolder holder);

    /// <inheritdoc/>
    public virtual object GetArgument(int index) => throw new ArgumentOutOfRangeException(message: "The request has zero arguments", null);

    /// <inheritdoc/>
    public virtual void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(message: "The request has zero arguments", null);

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <inheritdoc/>
    public abstract string GetMethodName();

    /// <inheritdoc/>
    public abstract string GetInterfaceName();

    /// <inheritdoc/>
    public abstract string GetActivityName();

    /// <inheritdoc/>
    public abstract Type GetInterfaceType();

    /// <inheritdoc/>
    public abstract MethodInfo GetMethod();

    /// <inheritdoc/>
    public override string ToString() => IRequest.ToString(this);

    // 因本类上的 [SelfInvokingReturnType(nameof(InitializeRequest))] 特性，创建该请求的 Grain 引用会在生成代码中创建请求时调用此方法。
    public DurableTask InitializeRequest(GrainReference targetGrainReference)
    {
        // 捕获请求上下文。
        Context = new()
        {
            // TaskId 稍后会在提交之前通过调用位置的扩展方法填充。
            TargetId = targetGrainReference.GrainId,
        };
        return this;
    }

    ValueTask<DurableTaskResponse> ISchedulableTask.ScheduleAsync(TaskId taskId, CancellationToken cancellationToken) =>
        ScheduleRootAsync(_shared, this, taskId, cancellationToken);

    IScheduledTaskHandle ISchedulableTask.Post(TaskId taskId) => PostRoot(_shared, this, taskId);

    /// <summary>
    /// 从持久化执行之外运行的代码调度根任务，并在目标 Grain
    /// 接受并持久化存储该任务后完成。
    /// </summary>
    /// <remarks>
    /// 调用像客户端调用一样直接发送到目标。在目标 Grain 上运行的代码通过
    /// Grain 自身的运行时调度，因此不可重入的 Grain 不会调用自身。
    /// </remarks>
    internal static ValueTask<DurableTaskResponse> ScheduleRootAsync(
        DurableTaskRequestShared shared,
        IDurableTaskRequest request,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        var context = IDurableTaskRequest.GetContext(request);
        PrepareClientContext(context);
        return GetServer(shared, context.TargetId).ScheduleAsync(taskId, request, cancellationToken);
    }

    /// <summary>
    /// 在运行调用代码的 Grain 的持久化发件箱中暂存根任务的启动操作，并返回
    /// 任务的句柄。
    /// </summary>
    /// <remarks>启动操作随 Grain 的下一次日志写入提交。</remarks>
    /// <exception cref="InvalidOperationException">调用代码未在支持持久化任务的 Grain 中运行。</exception>
    internal static IScheduledTaskHandle PostRoot(DurableTaskRequestShared shared, IDurableTaskRequest request, TaskId taskId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(taskId, default);
        if (!TryGetRuntime(shared, out var runtime))
        {
            throw new InvalidOperationException(
                "Post stages a start in the durable outbox of the calling grain, and this caller has none. Use ScheduleAsync, which completes once the task's host has accepted the task.");
        }

        runtime.PostRoot(taskId, request);
        return GetHandle(shared, request, taskId);
    }

    /// <summary>获取请求目标上的根任务 <paramref name="taskId"/> 的句柄。</summary>
    internal static IScheduledTaskHandle GetHandle(DurableTaskRequestShared shared, IDurableTaskRequest request, TaskId taskId)
    {
        var target = IDurableTaskRequest.GetContext(request).TargetId;
        return new GrainScheduledTaskHandle(taskId, target, request, GetServer(shared, target), lastResponse: null);
    }

    /// <summary>
    /// 获取 <paramref name="target"/> 的任务的服务器：若运行调用代码的 Grain 是目标，则为该 Grain 的运行时；
    /// 否则为目标的 Grain 扩展。
    /// </summary>
    private static IDurableTaskServer GetServer(DurableTaskRequestShared shared, GrainId target) =>
        TryGetRuntime(shared, out var runtime)
        && runtime is IDurableTaskServer local
        && shared.GrainContextAccessor.GrainContext.GrainId == target
            ? local
            : shared.GrainFactory.GetGrain<IDurableTaskGrainExtension>(target);

    /// <inheritdoc/>
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext executionContext) =>
        RunRemoteAsync(_shared, this, executionContext);

    /// <summary>
    /// 将 <paramref name="request"/> 作为 <paramref name="executionContext"/> 的操作运行；后者是由非 Grain 宿主运行的
    /// 持久化执行实例：在目标上以该执行的标识符调度请求，并轮询
    /// 目标直到任务完成。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 调度是幂等的：如果目标已经在该标识符下保存了具有相同请求的任务，
    /// 再次接受该调用，因此重新运行的执行实例会重新附接到同一任务。Grain 的执行实例暂存其
    /// 对其他 Grain 的调用到 Grain 的持久化发件箱中，而不是直接执行。
    /// </para>
    /// <para>
    /// 将执行实例的任务取消请求传达给目标任务，由目标任务自行决定结果，所以轮询继续
    /// 进行直到目标任务完成。取消执行尝试则结束等待。
    /// </para>
    /// </remarks>
    internal static async ValueTask<DurableTaskResponse> RunRemoteAsync(
        DurableTaskRequestShared shared,
        IDurableTaskRequest request,
        DurableTaskContext executionContext)
    {
        var context = request.Context;
        Debug.Assert(context is not null);
        PrepareClientContext(context);

        var remote = shared.GrainFactory.GetGrain<IDurableTaskGrainExtension>(context.TargetId);
        var response = await remote.ScheduleAsync(executionContext.Id, request, executionContext.AttemptCancellationToken);
        var options = new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromSeconds(5) };
        var cancellationRequested = false;
        while (!response.IsCompleted)
        {
            if (!cancellationRequested && executionContext.IsTaskCancellationRequested)
            {
                cancellationRequested = true;
                await GrainScheduledTaskHandle.RequestCancellationAsync(remote, executionContext.Id, executionContext.TaskCancellationReason);
            }

            try
            {
                // 尚未向目标任务传递取消请求时，任务取消令牌可中断轮询，以便及时向目标发出取消请求。
                var token = cancellationRequested ? executionContext.AttemptCancellationToken : executionContext.CancellationToken;
                response = await DurableTaskPolling.PollAsync(remote, executionContext.Id, options, token);
            }
            catch (OperationCanceledException) when (!cancellationRequested && !executionContext.AttemptCancellationToken.IsCancellationRequested)
            {
                // 已请求任务取消，下一次迭代将向目标任务发出该取消请求。
            }
        }

        return response;
    }
    /// <inheritdoc/>
    ValueTask<Response> IInvokable.Invoke()
        // 可以设法让这里正常工作，例如选取一个随机的任务 ID。
        => throw new NotImplementedException("Durable task requests can not be invoked directly");

    /// <inheritdoc/>
    //ValueTask<DurableTaskResponse> IDurableTaskRequest.InvokeImplementation(DurableTaskContext executionContext) => DurableTaskRuntimeHelper.RunAsync(InvokeInner(), executionContext);
    DurableTask IDurableTaskRequest.CreateTask() => InvokeInner();

    // 生成的代码。此方法直接调用目标方法。
    protected abstract DurableTask InvokeInner();

    internal static bool TryGetRuntime(
        DurableTaskRequestShared shared,
        [NotNullWhen(true)] out IDurableTaskGrainRuntime? runtime)
    {
        // 在 Grain 之外，Silo 解析托管客户端；其组件来自 Silo 的根服务，
        // 无法构造激活实例作用域的运行时；这样的调用方像外部客户端一样调度任务。
        var context = shared.GrainContextAccessor.GrainContext;
        if (context.GrainId.IsClient() || context.GetComponent<IDurableTaskGrainRuntime>() is not { } localProxy)
        {
            runtime = null;
            return false;
        }

        runtime = localProxy;
        return true;
    }

    internal static void PrepareClientContext(DurableTaskRequestContext context)
    {
        context.CallerId = default;
        context.SupportsDurableCompletion = false;
    }

    /// <inheritdoc/>
    public virtual TimeSpan? GetDefaultResponseTimeout() => null;

    public IScheduledTaskHandle GetHandle(TaskId taskId) => DurableTaskRequest.GetHandle(_shared, this, taskId);
}

/// <summary>
/// 表示调度返回 <see cref="DurableTask{TResult}"/> 的方法的请求。
/// </summary>
[GenerateSerializer]
[ReturnValueProxy(initializerMethodName: nameof(InitializeRequest))]
[Alias("DurableTaskRequest`1")]
[method: GeneratedActivatorConstructor]
public abstract class DurableTaskRequest<TResult>(DurableTaskRequestShared shared) : DurableTask<TResult>, IDurableTaskRequest, ISchedulableTask
{
    // 注意：这里可以使用 RuntimeContext 来省去一个字段，但这样需要使内部成员对本程序集可见。
    // 暂时不这样做，以确认不依赖它也能完成大部分工作，从而展示 Orleans 的可扩展性。
    // 即使不推荐这种方式，将来某个时候也可能值得把 RuntimeContext 公开。
    [NonSerialized]
    private readonly DurableTaskRequestShared _shared = shared;

    /// <inheritdoc/>
    [Id(0)]
    public DurableTaskRequestContext? Context { get; private set; }

    /// <summary>
    /// 获取调用选项。
    /// </summary>
    [field: NonSerialized]
    public InvokeMethodOptions Options { get; private set; }

    /// <inheritdoc/>
    public virtual int GetArgumentCount() => 0;

    /// <summary>
    /// 合并提供的调用选项。
    /// </summary>
    /// <param name="options">
    /// 选项。
    /// </param>
    public void AddInvokeMethodOptions(InvokeMethodOptions options) => Options |= options;

    /// <inheritdoc/>
    public abstract object GetTarget();

    /// <inheritdoc/>
    public abstract void SetTarget(ITargetHolder holder);

    /// <inheritdoc/>
    public virtual object GetArgument(int index) => throw new ArgumentOutOfRangeException(message: "The request has zero arguments", null);

    /// <inheritdoc/>
    public virtual void SetArgument(int index, object value) => throw new ArgumentOutOfRangeException(message: "The request has zero arguments", null);

    /// <inheritdoc/>
    public abstract void Dispose();

    /// <inheritdoc/>
    public abstract string GetMethodName();

    /// <inheritdoc/>
    public abstract string GetInterfaceName();

    /// <inheritdoc/>
    public abstract string GetActivityName();

    /// <inheritdoc/>
    public abstract Type GetInterfaceType();

    /// <inheritdoc/>
    public abstract MethodInfo GetMethod();

    /// <inheritdoc/>
    public override string ToString() => IRequest.ToString(this);

    // 因本类上的 [SelfInvokingReturnType(nameof(InitializeRequest))] 特性，创建该请求的 Grain 引用会在生成代码中创建请求时调用此方法。
    public DurableTask<TResult> InitializeRequest(GrainReference targetGrainReference)
    {
        // 捕获请求上下文。
        Context = new()
        {
            // TaskId 稍后会在提交之前通过调用位置的扩展方法填充。
            TargetId = targetGrainReference.GrainId,
        };
        return this;
    }

    /// <inheritdoc/>
    public ValueTask<DurableTaskResponse> ScheduleAsync(TaskId taskId, CancellationToken cancellationToken = default) =>
        DurableTaskRequest.ScheduleRootAsync(_shared, this, taskId, cancellationToken);

    IScheduledTaskHandle ISchedulableTask.Post(TaskId taskId) => DurableTaskRequest.PostRoot(_shared, this, taskId);

    /// <inheritdoc/>
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext executionContext) =>
        DurableTaskRequest.RunRemoteAsync(_shared, this, executionContext);
    /// <inheritdoc/>
    ValueTask<Response> IInvokable.Invoke() => throw new NotImplementedException("Durable task requests can not be invoked directly");

    /// <inheritdoc/>
    //ValueTask<DurableTaskResponse> IDurableTaskRequest.InvokeImplementation(DurableTaskContext executionContext) => DurableTaskRuntimeHelper.RunAsync(InvokeInner(), executionContext);
    DurableTask IDurableTaskRequest.CreateTask() => InvokeInner();

    // 生成的代码。此方法直接调用目标方法。
    protected abstract DurableTask<TResult> InvokeInner();

    /// <inheritdoc/>
    public virtual TimeSpan? GetDefaultResponseTimeout() => null;

    public IScheduledTaskHandle GetHandle(TaskId taskId) => DurableTaskRequest.GetHandle(_shared, this, taskId);
}

/// <summary><paramref name="target"/> 上的根任务的句柄，由持久化执行实例之外的代码观察。</summary>
internal sealed class GrainScheduledTaskHandle(
    TaskId taskId,
    GrainId target,
    IDurableTaskRequest? request,
    IDurableTaskServer grain,
    DurableTaskResponse? lastResponse) : IScheduledTaskHandle
{
    public TaskId TaskId { get; } = taskId;

    /// <summary>返回任务的 <see cref="DurableTaskAddress"/>，类型为 <see cref="IDurableTaskReference"/>。</summary>
    public TFeature? GetFeature<TFeature>()
        where TFeature : class => new DurableTaskAddress(target, TaskId) as TFeature;
    public DurableTaskResponse? LastResponse { get; private set; } = lastResponse;

    /// <remarks>取消是幂等的，因此未能访问 Grain 的失败会像轮询那样重试。</remarks>
    public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) =>
        DurableTaskPolling.SendAsync(
            grain,
            (TaskId, reason),
            static (grain, state) => RequestCancellationAsync(grain, state.TaskId, state.reason),
            cancellationToken);

    /// <summary>请求取消任务 <paramref name="taskId"/>（位于 <paramref name="grain"/> 上），并携带 <paramref name="reason"/>。</summary>
    /// <remarks>不携带原因的请求使用所有版本的 Grain 均支持的原有操作。</remarks>
    internal static ValueTask RequestCancellationAsync(IDurableTaskServer grain, TaskId taskId, string? reason) =>
        reason is null
            ? grain.CancelAsync(taskId, CancellationToken.None)
            : grain.CancelAsync(taskId, reason, CancellationToken.None);

    /// <remarks>中止是幂等的，因此未能访问 Grain 的失败会像轮询那样重试。</remarks>
    public ValueTask AbortAsync(string reason, CancellationToken cancellationToken) =>
        DurableTaskPolling.SendAsync(
            grain,
            (TaskId, reason),
            static (grain, state) => grain.AbortAsync(state.TaskId, state.reason, CancellationToken.None),
            cancellationToken);

    public async ValueTask<DurableTaskResponse> PollAsync(PollingOptions options, CancellationToken cancellationToken)
    {
        if (LastResponse is { IsCompleted: true } response)
        {
            return response;
        }

        var pollOptions = new SubscribeOrPollOptions { PollTimeout = options.PollTimeout };
        return DecodeResponse(await DurableTaskPolling.PollAsync(grain, TaskId, pollOptions, cancellationToken));
    }

    public async ValueTask<DurableTaskResponse> ScheduleAsync(CancellationToken cancellationToken)
    {
        return await grain.ScheduleAsync(
            TaskId,
            request ?? throw new InvalidOperationException("An attached durable task cannot be scheduled."),
            cancellationToken);
    }

    public async ValueTask<DurableTaskResponse> WaitAsync(CancellationToken cancellationToken)
    {
        if (LastResponse is { IsCompleted: true } response)
        {
            return response;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new SubscribeOrPollOptions { PollTimeout = TimeSpan.FromSeconds(5) };
            response = DecodeResponse(await DurableTaskPolling.PollAsync(grain, TaskId, options, cancellationToken));
            if (response.IsCompleted)
            {
                return response;
            }
        }
    }

    private DurableTaskResponse DecodeResponse(DurableTaskResponse response)
    {
        return LastResponse = response;
    }
}
