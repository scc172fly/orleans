using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Journaling;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Durable.Http;

/// <summary>
/// HTTP 操作的主机为 HTTP 操作: 根任务, 根任务订阅执行运行操作的任务并完成
/// 与这项任务的回应。
/// </summary>
/// <remarks>
/// <para>
/// grain的密钥是操作的密钥,关键出自其范围、调用者及其识别符号,因此每个请求
/// 哪个名称的操作名称将实现此 grain ,而不知道是哪个 grain 运行该操作的工作。
/// 结果是工作终端的反应,包括故障或注销,因此操作报告准确无误。
/// </para>
/// <para>
/// 工作是其 grain上的一项任务:一个耐用 grain 调用,主机以该调用点名的分离根开始
/// 或现有任务中的某个引用名称。主机日志将工作记录在 grain
/// 请求哪个信号或取消操作
/// 并直接处理它的任务。
/// </para>
/// </remarks>
[Alias("Orleans.Durable.Http.IDurableOperationGrain")]
internal interface IDurableOperationGrain : IGrainWithStringKey
{
    /// <summary>运行 <paramref name="call"/> 作为操作的工作,并返回其终端响应。</summary>
    /// <param name="call">
    /// 生成的耐久 grain-呼叫请求,该呼叫按其类型顺序排列。
    /// 创建,所以呼叫通过它 没有副本。
    /// </param>
    [Alias("Run")]
    DurableTask<DurableTaskResponse> RunAsync([Immutable] DurableTask call);

    /// <summary>订阅 <paramref name="address"/>的现有任务,并返回其终端响应。</summary>
    [Alias("Subscribe")]
    DurableTask<DurableTaskResponse> SubscribeAsync(DurableTaskAddress address);

    /// <summary>
    /// 一旦主机日记可持久记录,或当不记录时返回 <see langword="null"/> ,则返回操作的作品
    /// <paramref name="timeout"/>内,或启动先停止。
    /// </summary>
    /// <remarks>
    /// 日记在工作(Q0Q)时记录工作,一旦该工作由它 grain 承担,即提出请求,然后发出信号或取消工作
    /// 操作,如果主机未能将工作记录为 <see cref="DurableOperationWorkKind.NotStarted"/>。
    /// </remarks>
    [Alias("WaitForWork"), Orleans.Concurrency.AlwaysInterleave]
    Task<DurableOperationWork?> WaitForWorkAsync(TimeSpan timeout);
}

/// <summary>确定行动的工作类型</summary>
[GenerateSerializer]
[Alias("Orleans.Durable.Http.DurableOperationWorkKind")]
internal enum DurableOperationWorkKind
{
    /// <summary>耐性 grain 呼叫,操作运行。</summary>
    Run,

    /// <summary>
    /// 耐性 grain 操作主机无法启动此操作的 grain:呼声从未运行,或是否
    /// 未知运行的错误报告,因此该选项被设置为未知。
    /// </summary>
    NotStarted,

    /// <summary>混合行动所认同的、其他当事方拥有的现有任务。</summary>
    Subscription,
}

/// <summary>确定操作的工作:操作进行什么,以及执行的任务。</summary>
/// <param name="Kind">那种工作</param>
/// <param name="Target">下 grain 工作的任务。</param>
/// <param name="Method">
/// 调用时,它所引用的界面方法的身份、其可援引的格式类型;否则是空的。
/// </param>
/// <param name="TaskId">
/// 工作任务: <paramref name="Target"/>,或 <see langword="null"/> ,要求发出一项通知,而请求在
/// 任务存在, 并且工作从来就没有开始过。
/// </param>
/// <remarks>
/// 当工作在相同的 grain上调用同一方法或订阅相同任务时,与其他工作相同。
/// 持久任务运行时间确定操作:根据它们的工作,而不是依据其论点如何顺序排列。
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Http.DurableOperationWork")]
internal sealed record DurableOperationWork(
    [property: Id(0)] DurableOperationWorkKind Kind,
    [property: Id(1)] GrainId Target,
    [property: Id(2)] string Method,
    [property: Id(3)] TaskId? TaskId)
{
    /// <summary>获得工作任务地址,或 <see langword="null"/> 工作从来就没有开始过</summary>
    public DurableTaskAddress? Address => TaskId is { } taskId ? new(Target, taskId) : null;

    /// <summary>了解操作是否同意其他当事方拥有的现有任务。</summary>
    public bool IsSubscription => Kind == DurableOperationWorkKind.Subscription;

    /// <summary>返回 <paramref name="work"/>,即呼叫或提及现有任务,请描述。</summary>
    public static DurableOperationWork Describe(DurableTask work) => work switch
    {
        IDurableTaskReference reference => new(DurableOperationWorkKind.Subscription, reference.Target, string.Empty, reference.Id),
        _ when work.TryGetTarget(out var target) => new(DurableOperationWorkKind.Run, target, RuntimeTypeNameFormatter.Format(work.GetType()), TaskId: null),
        _ => throw new InvalidOperationException(
            $"A durable operation runs a durable grain call, such as 'grain.MethodAsync(...)', or subscribes to an existing task, which 'grain.GetDurableTask<T>(id)' references, not '{work.GetType()}'."),
    };

    /// <summary>返回这项工作是否履行 <paramref name="other"/> 履行的工作。</summary>
    public bool IsSameWorkAs(DurableOperationWork other) => IsSubscription
        ? other.IsSubscription && Target == other.Target && TaskId == other.TaskId
        : !other.IsSubscription && Target == other.Target && Method == other.Method;

    /// <summary>
    /// 将工作格式化为 <c>{kind}{grain length}:{grain}{method length}:{method}{task}</c>,其中的类型是 <c>r</c>,
    /// <c>n</c>,或 <c>s</c>, 其中 <see cref="Parse"/> 读起来。
    /// </summary>
    /// <remarks>该日记存储文本, 每份日记格式都将文字序列化, 而不配置应用程序 。</remarks>
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
    /// <summary>运行所在主机上任务根标识符。</summary>
    public const string RootId = "operation";

    /// <summary>东道方订阅业务工作的识别数据部分。</summary>
    private const string SubscriptionId = "work";

    public async DurableTask<DurableTaskResponse> RunAsync(DurableTask call)
    {
        // 调用以根根运行 grain,以操作的钥匙命名,这是在
        // 调用 grain。一旦有 grain ,时间安排将返回它,因此日记记录的工作就是
        // 发出或取消该信号的请求可以满足。
        var described = DurableOperationWork.Describe(call);
        ScheduledTask root;
        try
        {
            root = await call.Detached(this.GetPrimaryKeyString()).ScheduleAsync();
        }
        catch (Exception exception) when (DurableOperationStartException.FromScheduleFailure(exception) is { } failure)
        {
            // 日记记录说工作不是从手术失败开始 所以请求等待
            // 工作结束。
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
            // 承诺的日志一旦被认可,即提供日记的价值,当日记一经确认,即结束
            // 启动停止,所以答案总是持久的。
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
        // 工作骑行时使用订阅日记写。 重播会指定相同值, 所以它没有写新内容 。
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
