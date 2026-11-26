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
/// 系长期任务请求。
/// </summary>
public interface IDurableTaskRequest : IRequest
{
    /// <summary>
    /// 获取任务请求的背景。
    /// </summary>
    DurableTaskRequestContext? Context { get; }

    /// <summary>
    /// 启动目标方的方法
    /// </summary>
    /// <returns>援引的结果。</returns>
    //ValueTask<DurableTaskResponse> InvokeImplementation(DurableTaskContext executionContext);
    DurableTask CreateTask();

    /// <summary>
    /// 返回请求的字符串表示。
    /// </summary>
    /// <returns>请求的字符串表示。</returns>
    public string ToMethodCallString() => ToMethodCallString(this);

    /// <summary>
    /// 返回 <paramref name="request"/>环境的上下文 grain 创建请求集的参考文献。
    /// </summary>
    /// <exception cref="InvalidOperationException">请求没有上下文。</exception>
    internal static DurableTaskRequestContext GetContext(IDurableTaskRequest request) =>
        request.Context
        ?? throw new InvalidOperationException(
            $"Durable task request '{request.GetType()}' has no context. A grain reference creates durable task requests with their context.");

    /// <summary>
    /// 返回 <paramref name="request"/> 引用的界面方法的身份:其格式型号
    /// 代码生成器每个接口方法一次发出 不可置疑
    /// </summary>
    /// <remarks>
    /// 操作的重播和根据标识符重试根任务,在援引时是相同的
    /// 同一目标上的相同方法。 身份不包含参数, 所以一个部署改变了如何
    /// 重复重复同样的操作,例如通过添加一个可选字段来进行序列化的参数。
    /// </remarks>
    internal static string GetMethod(IDurableTaskRequest request) => RuntimeTypeNameFormatter.Format(request.GetType());

    /// <summary>
    /// 返回 <paramref name="request"/>可引用类型和序列参数的散列,该点数由诊断报告。
    /// </summary>
    /// <remarks>
    /// 每当一个参数的序列形式发生变化时,散列就会变化,因此它永远无法决定是否提出两项请求
    /// 相同的操作: <see cref="GetMethod"/> 具有。
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
    // 注:我们可以通过使用 RuntimeContext来保存字段,但这需要使内装件可见到本组群。
    // 现今我们并没有这样做, 只是为了确保我们能走得远而不需要它, 证明 Orleans的可扩展性。
    // 即便不是建议的办法,在某个时候公布 RuntimeContext 或许值得。
    [NonSerialized]
    private readonly DurableTaskRequestShared _shared = shared;

    /// <inheritdoc />
    [Id(0)]
    public DurableTaskRequestContext? Context { get; private set; }

    /// <summary>
    /// 获得援引选项。
    /// </summary>
    [field: NonSerialized]
    public InvokeMethodOptions Options { get; private set; }

    /// <inheritdoc/>
    public virtual int GetArgumentCount() => 0;

    /// <summary>
    /// 纳入了所提供的调用选项。
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

    // 创建时呼吁创建生成代码 grain 以[SelfInvokingReturnType(姓名( )InitializeRequest() 属于本类。
    public DurableTask InitializeRequest(GrainReference targetGrainReference)
    {
        // 获取请求内容 。
        Context = new()
        {
            // TaskId 将在以后提交前通过呼叫站点的推广方法填写。
            TargetId = targetGrainReference.GrainId,
        };
        return this;
    }

    ValueTask<DurableTaskResponse> ISchedulableTask.ScheduleAsync(TaskId taskId, CancellationToken cancellationToken) =>
        ScheduleRootAsync(_shared, this, taskId, cancellationToken);

    IScheduledTaskHandle ISchedulableTask.Post(TaskId taskId) => PostRoot(_shared, this, taskId);

    /// <summary>
    /// 将一个在耐久执行之外运行的代码中的根任务安排在可执行范围之后,一旦目标 grain 达到完成
    /// 被接受并长期保存此项任务。
    /// </summary>
    /// <remarks>
    /// 电话直接接到目标,就像客户的一样 grain 附表通过
    /// grain自己的运行时间, 所以一个非返回 grain 的人不会自呼自叫。
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
    /// 将根任务在运行调用代码的 grain 耐久发件箱中开始阶段,并返回
    /// 任务的任务处理。
    /// </summary>
    /// <remarks>启动承诺, 以 grain下个日记写入 。</remarks>
    /// <exception cref="InvalidOperationException">调用代码没有在有耐久任务的 grain 中运行。</exception>
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

    /// <summary>获取请求目标根任务 <paramref name="taskId"/> 的控点。</summary>
    internal static IScheduledTaskHandle GetHandle(DurableTaskRequestShared shared, IDurableTaskRequest request, TaskId taskId)
    {
        var target = IDurableTaskRequest.GetContext(request).TargetId;
        return new GrainScheduledTaskHandle(taskId, target, request, GetServer(shared, target), lastResponse: null);
    }

    /// <summary>
    /// 获取 <paramref name="target"/>任务服务器的配置:运行调用代码的 grain 运行时间,当它
    /// 目标,或目标的 grain 扩展。
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
    /// 运行运行 <paramref name="request"/> 业务的 <paramref name="executionContext"/>执行,一个持久的执行
    /// 运行 grain 以外的主机:将请求表在执行的标识符下的目标上,并进行投票
    /// 目标直到任务完成
    /// </summary>
    /// <remarks>
    /// <para>
    /// 排期是一等的:目标已经按照识别特征承担任务,并提出了同样的要求,
    /// 再次接受它,所以执行时再次重复对同一任务进行连接。 A grain处决阶段
    /// 调用其他电话 grains 中在 grain耐久发箱代替。
    /// </para>
    /// <para>
    /// 要求取消执行部门的任务,是要求取消目标任务,而目标任务决定结果,因此继续投票
    /// 取消执行的尝试结束了等待
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
                // 在要求取消任务之前,其象征性结束投票,以便迅速提出请求。
                var token = cancellationRequested ? executionContext.AttemptCancellationToken : executionContext.CancellationToken;
                response = await DurableTaskPolling.PollAsync(remote, executionContext.Id, options, token);
            }
            catch (OperationCanceledException) when (!cancellationRequested && !executionContext.AttemptCancellationToken.IsCancellationRequested)
            {
                // 要求取消任务,这是目标任务的下一个迭代要求。
            }
        }

        return response;
    }
    /// <inheritdoc/>
    ValueTask<Response> IInvokable.Invoke()
        // 这样做可以有效... 比如说,也许选择一个随机任务代号
        => throw new NotImplementedException("Durable task requests can not be invoked directly");

    /// <inheritdoc/>
    //ValueTask<DurableTaskResponse> IDurableTaskRequest.InvokeImplementation(DurableTaskContext executionContext) => DurableTaskRuntimeHelper.RunAsync(InvokeInner(), executionContext);
    DurableTask IDurableTaskRequest.CreateTask() => InvokeInner();

    // 生成。 这直接引用了目标方法 。
    protected abstract DurableTask InvokeInner();

    internal static bool TryGetRuntime(
        DurableTaskRequestShared shared,
        [NotNullWhen(true)] out IDurableTaskGrainRuntime? runtime)
    {
        // a grain之外, silo 解决了主机客户的用户,其组成部分来自 silo根服务,
        // 无法构建激活范围运行时间; 调用器时间表像外部客户端一样 。
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
/// 表示要求排出一个 <see cref="DurableTask{TResult}"/>-返回方法。
/// </summary>
[GenerateSerializer]
[ReturnValueProxy(initializerMethodName: nameof(InitializeRequest))]
[Alias("DurableTaskRequest`1")]
[method: GeneratedActivatorConstructor]
public abstract class DurableTaskRequest<TResult>(DurableTaskRequestShared shared) : DurableTask<TResult>, IDurableTaskRequest, ISchedulableTask
{
    // 注:我们可以通过使用 RuntimeContext来保存字段,但这需要使内装件可见到本组群。
    // 现今我们并没有这样做, 只是为了确保我们能走得远而不需要它, 证明 Orleans的可扩展性。
    // 即便不是建议的办法,在某个时候公布 RuntimeContext 或许值得。
    [NonSerialized]
    private readonly DurableTaskRequestShared _shared = shared;

    /// <inheritdoc/>
    [Id(0)]
    public DurableTaskRequestContext? Context { get; private set; }

    /// <summary>
    /// 获得援引选项。
    /// </summary>
    [field: NonSerialized]
    public InvokeMethodOptions Options { get; private set; }

    /// <inheritdoc/>
    public virtual int GetArgumentCount() => 0;

    /// <summary>
    /// 纳入了所提供的调用选项。
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

    // 创建时呼吁创建生成代码 grain 以[SelfInvokingReturnType(姓名( )InitializeRequest() 属于本类。
    public DurableTask<TResult> InitializeRequest(GrainReference targetGrainReference)
    {
        // 获取请求内容 。
        Context = new()
        {
            // TaskId 将在以后提交前通过呼叫站点的推广方法填写。
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

    // 生成。 这直接引用了目标方法 。
    protected abstract DurableTask<TResult> InvokeInner();

    /// <inheritdoc/>
    public virtual TimeSpan? GetDefaultResponseTimeout() => null;

    public IScheduledTaskHandle GetHandle(TaskId taskId) => DurableTaskRequest.GetHandle(_shared, this, taskId);
}

/// <summary>根任务 <paramref name="target"/>的处理,该根任务在耐用执行观察之外编码。</summary>
internal sealed class GrainScheduledTaskHandle(
    TaskId taskId,
    GrainId target,
    IDurableTaskRequest? request,
    IDurableTaskServer grain,
    DurableTaskResponse? lastResponse) : IScheduledTaskHandle
{
    public TaskId TaskId { get; } = taskId;

    /// <summary>返回任务 <see cref="DurableTaskAddress"/>,作为 <see cref="IDurableTaskReference"/>。</summary>
    public TFeature? GetFeature<TFeature>()
        where TFeature : class => new DurableTaskAddress(target, TaskId) as TFeature;
    public DurableTaskResponse? LastResponse { get; private set; } = lastResponse;

    /// <remarks>取消是无能的,所以无法达到 grain 调查结果会重新调查。</remarks>
    public ValueTask CancelAsync(string? reason, CancellationToken cancellationToken) =>
        DurableTaskPolling.SendAsync(
            grain,
            (TaskId, reason),
            static (grain, state) => RequestCancellationAsync(grain, state.TaskId, state.reason),
            cancellationToken);

    /// <summary>请求取消 <paramref name="taskId"/> 日期 时 <paramref name="grain"/>,携带 <paramref name="reason"/>.</summary>
    /// <remarks>请求无任何理由无理由地使用最初作业,而最初作业是《联合国 grain 服务。</remarks>
    internal static ValueTask RequestCancellationAsync(IDurableTaskServer grain, TaskId taskId, string? reason) =>
        reason is null
            ? grain.CancelAsync(taskId, CancellationToken.None)
            : grain.CancelAsync(taskId, reason, CancellationToken.None);

    /// <remarks>中止是一无所有,所以无法到达 grain 的状态会随着民意调查重新被查获。</remarks>
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
