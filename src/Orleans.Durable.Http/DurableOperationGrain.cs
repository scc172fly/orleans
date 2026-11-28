using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Durable.Http;

/// <summary>
/// 承载一个 HTTP 操作：一个根任务，订阅执行操作工作的任务，并以
/// 该任务的响应完成。
/// </summary>
/// <remarks>
/// <para>
/// 此 Grain 的键即操作的键，由操作的作用域、调用方和标识符派生，因此所有
/// 指定该操作的请求均可到达此 Grain，无需知道由哪个 Grain 执行操作的工作。根任务的
/// 结果即工作的终态响应（包括失败或取消），因此操作能够如实报告结果。
/// </para>
/// <para>
/// 工作是 Grain 上的任务，即持久化 Grain 调用；宿主将其作为以
/// 操作键命名的分离式根任务启动；工作也可以是由引用指定的现有任务。当执行工作的 Grain
/// 保存任务后，宿主的日志即记录该工作；这是该工作的唯一记录。向操作发送信号或取消操作的请求
/// 从日志读取该工作，并直接寻址其任务。
/// </para>
/// </remarks>
[Alias("Orleans.Durable.Http.IDurableOperationGrain")]
internal interface IDurableOperationGrain : IGrainWithStringKey
{
    /// <summary>将 <paramref name="call"/> 作为操作的工作运行，并返回其终态响应。</summary>
    /// <param name="call">
    /// 生成的持久化 Grain 调用请求，调用按其自身类型进行序列化。请求创建后不可变，
    /// 因此调用在传递该请求时不进行复制。
    /// </param>
    [Alias("Run")]
    DurableTask<DurableTaskResponse> RunAsync([Immutable] DurableTask call);

    /// <summary>订阅位于 <paramref name="address"/> 的现有任务，并返回其终态响应。</summary>
    [Alias("Subscribe")]
    DurableTask<DurableTaskResponse> SubscribeAsync(DurableTaskAddress address);

    /// <summary>
    /// 宿主日志持久化记录操作的工作后返回该工作；若未能完成记录，则返回 <see langword="null"/>，
    /// 具体而言，等待超过 <paramref name="timeout"/> 或激活实例提前停止时返回该空值。
    /// </summary>
    /// <remarks>
    /// 执行工作的 Grain 保存任务后，日志即记录该工作，因此后续发送信号或取消工作的请求
    /// 能够到达该工作。宿主未能启动工作的操作将其记录为 <see cref="DurableOperationWorkKind.NotStarted"/>。
    /// </remarks>
    [Alias("WaitForWork"), Orleans.Concurrency.AlwaysInterleave]
    Task<DurableOperationWork?> WaitForWorkAsync(TimeSpan timeout);
}

/// <summary>标识操作的工作类型。</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableOperationWorkKind")]
internal enum DurableOperationWorkKind
{
    /// <summary>操作执行的持久化 Grain 调用。</summary>
    Run,

    /// <summary>
    /// 操作宿主未能在其 Grain 上启动的持久化 Grain 调用：调用从未运行，或无法确定是否
    /// 运行，具体情况由操作的错误信息报告。
    /// </summary>
    NotStarted,

    /// <summary>操作所订阅的现有任务，由其他方拥有。</summary>
    Subscription,
}

/// <summary>标识操作的工作，即操作执行的内容及执行该内容的任务。</summary>
/// <param name="Kind">工作的类型。</param>
/// <param name="Target">保存工作任务的 Grain。</param>
/// <param name="Method">
/// 对于调用，为其调用的接口方法标识，即可调用对象类型的格式化表示；其他情况为空。
/// </param>
/// <param name="TaskId">
/// 位于 <paramref name="Target"/> 上的工作任务；对于请求描述调用但任务尚未存在的情况，此值为 <see langword="null"/>，
/// 对于从未启动的工作也是如此。
/// </param>
/// <remarks>
/// 如果工作在同一 Grain 上调用相同方法，或订阅相同任务，则与另一工作相同。这符合
/// 持久化任务运行时的操作标识规则：根据执行内容识别操作，而非根据参数的序列化方式。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableOperationWork")]
internal sealed record DurableOperationWork(
    [property: Id(0)] DurableOperationWorkKind Kind,
    [property: Id(1)] GrainId Target,
    [property: Id(2)] string Method,
    [property: Id(3)] TaskId? TaskId)
{
    /// <summary>获取工作任务的地址；若工作从未启动，则返回 <see langword="null"/>。</summary>
    public DurableTaskAddress? Address => TaskId is { } taskId ? new(Target, taskId) : null;

    /// <summary>获取操作是否订阅由其他方拥有的现有任务。</summary>
    public bool IsSubscription => Kind == DurableOperationWorkKind.Subscription;

    /// <summary>返回 <paramref name="work"/> 所描述的工作，即调用或对现有任务的引用。</summary>
    public static DurableOperationWork Describe(DurableTask work) => work switch
    {
        IDurableTaskReference reference => new(DurableOperationWorkKind.Subscription, reference.Target, string.Empty, reference.Id),
        _ when work.TryGetTarget(out var target) => new(DurableOperationWorkKind.Run, target, RuntimeTypeNameFormatter.Format(work.GetType()), TaskId: null),
        _ => throw new InvalidOperationException(
            $"A durable operation runs a durable grain call, such as 'grain.MethodAsync(...)', or subscribes to an existing task, which 'grain.GetDurableTask<T>(id)' references, not '{work.GetType()}'."),
    };

    /// <summary>返回此工作与 <paramref name="other"/> 执行的内容是否相同。</summary>
    public bool IsSameWorkAs(DurableOperationWork other) => IsSubscription
        ? other.IsSubscription && Target == other.Target && TaskId == other.TaskId
        : !other.IsSubscription && Target == other.Target && Method == other.Method;

    /// <summary>
    /// 将工作格式化为 <c>{kind}{grain length}:{grain}{method length}:{method}{task}</c>，其中类型为 <c>r</c>、
    /// <c>n</c> 或 <c>s</c>，由 <see cref="Parse"/> 读取。
    /// </summary>
    /// <remarks>日志存储此文本，各种日志格式均可直接序列化该文本，无需应用配置。</remarks>
    public string Format()
    {
        var kind = Kind switch
        {
            DurableOperationWorkKind.Run => 'r',
            DurableOperationWorkKind.NotStarted => 'n',
            _ => 's',
        };
        var grain = Target.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"{kind}{grain.Length}:{grain}{Method.Length}:{Method}{TaskId?.ToString()}");
    }

    public static DurableOperationWork Parse(string value)
    {
        var kind = value[0] switch
        {
            'r' => DurableOperationWorkKind.Run,
            'n' => DurableOperationWorkKind.NotStarted,
            _ => DurableOperationWorkKind.Subscription,
        };
        var position = 1;
        var grain = ReadField(value, ref position);
        var method = ReadField(value, ref position);
        return new(kind, GrainId.Parse(grain), method, position == value.Length ? null : Orleans.Durable.Tasks.TaskId.Parse(value[position..]));

        static string ReadField(string value, ref int position)
        {
            var separator = value.IndexOf(':', position);
            var length = int.Parse(value.AsSpan(position, separator - position), CultureInfo.InvariantCulture);
            position = separator + 1 + length;
            return value.Substring(separator + 1, length);
        }
    }
}
[GrainType("orleans-durable-operation")]
internal sealed class DurableOperationGrain(
    [FromKeyedServices("work")] IDurableValue<string?> work,
    IGrainFactory grainFactory) : DurableGrain, IDurableOperationGrain
{
    /// <summary>操作任务在其宿主上的根任务标识符。</summary>
    public const string RootId = "operation";

    /// <summary>宿主对操作工作的订阅的标识符段。</summary>
    private const string SubscriptionId = "work";

    public async DurableTask<DurableTaskResponse> RunAsync(DurableTask call)
    {
        // 调用在其 Grain 上作为根任务运行，使用操作的键命名，该键在调用同一 Grain 的操作中
        // 唯一。调度在 Grain 保存任务后即返回，因此日志随后记录的工作
        // 可被发送信号或取消工作的请求访问。
        var described = DurableOperationWork.Describe(call);
        ScheduledTask root;
        try
        {
            root = await call.Detached(this.GetPrimaryKeyString()).ScheduleAsync();
        }
        catch (Exception exception) when (DurableOperationStartException.FromScheduleFailure(exception) is { } failure)
        {
            // 日志在记录操作失败时同时记录工作未启动，因此等待工作出现的请求
            // 得以结束。
            work.Value = (described with { Kind = DurableOperationWorkKind.NotStarted }).Format();
            throw failure;
        }

        var recorded = root.TryGetAddress(out var address);
        Debug.Assert(recorded && address.Target == described.Target, "A detached root has an address on the call's grain.");
        return await SubscribeCoreAsync(described with { TaskId = address.Id });
    }

    public async DurableTask<DurableTaskResponse> SubscribeAsync(DurableTaskAddress address) =>
        await SubscribeCoreAsync(new(DurableOperationWorkKind.Subscription, address.Target, string.Empty, address.Id));

    public async Task<DurableOperationWork?> WaitForWorkAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            // 已提交的监视在日志写入得到确认后交付日志中的值，并在
            // 激活实例停止时结束，因此返回的结果始终具有持久性。
            await foreach (var change in work.WatchAsync(cancellation.Token))
            {
                if (change.Value is { } value)
                {
                    return DurableOperationWork.Parse(value);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        return null;
    }

    private async DurableTask<DurableTaskResponse> SubscribeCoreAsync(DurableOperationWork operationWork)
    {
        // 工作信息随订阅的日志写入一并保存。重放时赋予相同的值，因此不会产生新的写入。
        var value = operationWork.Format();
        if (work.Value != value)
        {
            work.Value = value;
        }

        var subscription = await grainFactory.GetDurableTask<object?>(operationWork.Address!.Value)
            .WithId(SubscriptionId)
            .ScheduleAsync();
        return await subscription.GetResponseAsync();
    }
}
