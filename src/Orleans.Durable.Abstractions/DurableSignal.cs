namespace Orleans.Durable.Tasks;

/// <summary>
/// 描述带有类型值的命名信号 <typeparamref name="T"/> 作为一项持久的任务,例如
/// 外部事件或人的批准。
/// </summary>
/// <typeparam name="T">信号值的类型。</typeparam>
/// <remarks>
/// <para>
/// 发件人和服务员共用一个描述符,通常是一个静态字段:
/// <c>static readonly DurableSignal&lt;Shipment&gt; Shipped = new("shipped");</c>。信号针对一项任务,即
/// 或引用了另一个客户端 grain ,任务执行过程中的任何等待都观察任务信号。
/// </para>
/// <para>
/// 每个名称都拉着每个任务的一个值:主机记录第一个值, 且每次等待该名称
/// 返回记录值,包括信号到达前开始的等待、信号到达后开始的等待,以及
/// 重放时等待。一个重复信号,其等值相等,使任务保持不变,而一个则具有不同值
/// 。使用 <see cref="For(string)"/> 为重复事件的每次发生得出一个不同的名称。
/// </para>
/// </remarks>
public sealed class DurableSignal<T>
{
    /// <summary>初始化一个名为 <paramref name="name"/>的信号描述符。</summary>
    /// <param name="name">信号名称,在一项任务中独有的信号。</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空的。</exception>
    public DurableSignal(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>获取信号名称。</summary>
    public string Name { get; }

    /// <summary>获得一个该信号发生时的描述符, 名称为 <c>{Name}/{suffix}</c>。</summary>
    /// <param name="suffix">发生时的稳定识别符号,如批准回合或行号。</param>
    /// <returns>带有衍生名称和相同值类型的描述符。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="suffix"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="suffix"/> 是空的。</exception>
    public DurableSignal<T> For(string suffix)
    {
        ArgumentException.ThrowIfNullOrEmpty(suffix);
        return new($"{Name}/{suffix}");
    }

    /// <summary>创建等待此信号并返回其值的定义。</summary>
    /// <returns>
    /// 在一个耐久执行的范围内, 以信号记录的价值完成的定义。
    /// 它将作为等待执行的一部分运行;作为输入到 <see cref="DurableTask.WhenAny(IReadOnlyList{DurableTask})"/>,
    /// 其竞争其他投入,例如 <see cref="DurableTask.DelayUntil(DateTimeOffset)"/> 超时 。
    /// </returns>
    /// <remarks>
    /// 持久取消等待执行的取消取消等待,除非等待被屏蔽
    /// <see cref="DurableTaskExtensions.Shielded{TResult}(DurableTask{TResult})"/>:一个屏蔽的等待接收信号
    /// 取消后到达, 只有中止结束, 终止才结束。 值是发送者为此提供的
    /// 信号类型; 不同的值类型会以 <see cref="InvalidCastException"/>失败等待。
    /// </remarks>
    public DurableTask<T> WaitAsync() => new SignalWaitDurableTask<T>(Name);

    /// <summary>创建一个等待此信号直到 <paramref name="deadline"/>的定义。</summary>
    /// <param name="deadline">等待停止的时间, 以主机逻辑时间计量。</param>
    /// <returns>
    /// 等待持久处决期间,作为执行的一部分而使用的定义
    /// <c>(true, value)</c> 当信号以 <paramref name="deadline"/>和
    /// <c>(false, default)</c> 当最后期限获胜时。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 定义是 <see cref="DurableTask.Race(IReadOnlyList{DurableTask})"/> 介间 <see cref="WaitAsync"/>, 和
    /// <c>0</c>和 <see cref="DurableTask.DelayUntil(DateTimeOffset)"/>,输入 <c>1</c>,这些都已经确定
    /// 其运行识别符号:指定 <c>WithId</c>的段,或执行令产生的段。
    /// 赢者是第一个完成的输入,主机在重放时选择了同一个赢者,所以重放返回
    /// 其它投入随后收到附有理由的持久取消请求
    /// <c>Another task won the race.</c>,而定义一旦两个输入都为终端,即完成后就完整了。 所以无论是等待还是等待
    /// 最后期限的延迟时间比它长
    /// </para>
    /// <para>
    /// 最后期限赢后,信号将保持其锁卡:一个后 <see cref="WaitAsync"/> 返回一个到达的值
    /// 等待处决的持久取消取消了比赛,除非种族被保护
    /// <see cref="DurableTaskExtensions.Shielded{TResult}(DurableTask{TResult})"/>保护了两个输入
    /// </para>
    /// </remarks>
    public DurableTask<(bool Received, T Value)> TryWaitAsync(DateTimeOffset deadline)
        => new CombinatorDurableTask<(bool Received, T Value)>(
            ScheduledTask.RaceOperation,
            [WaitAsync(), DurableTask.DelayUntil(deadline)],
            static async (inputs, context, operationId) =>
            {
                var (index, response) = await DurableTask.RaceAsync(inputs, context, operationId);
                if (index == 0)
                {
                    return (true, response.GetResult<T>());
                }

                response.EnsureSuccessfulCompletion();
                return (false, default!);
            });

    /// <inheritdoc />
    public override string ToString() => Name;
}

internal sealed class SignalWaitDurableTask<T>(string name) : DurableTask<T>
{
    protected internal override ValueTask<DurableTaskResponse> RunAsync(DurableTaskContext context)
    {
        context.ThrowIfCannotRunOperation();
        return context.ObserveAttempt(context.WaitForSignalAsync(name, context.InlineOperationCancellationToken));
    }

    internal override bool RunsInlineInParent => true;
}
