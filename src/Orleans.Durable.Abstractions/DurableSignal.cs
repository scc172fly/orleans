namespace Orleans.Durable.Tasks;

/// <summary>
/// 描述一种命名信号，该信号向持久化任务传入 <typeparamref name="T"/> 类型的值，例如
/// 外部事件或人工审批。
/// </summary>
/// <typeparam name="T">信号值的类型。</typeparam>
/// <remarks>
/// <para>
/// 发送方和等待方共享同一个描述符，通常为静态字段：
/// <c>static readonly DurableSignal&lt;Shipment&gt; Shipped = new("shipped");</c>。信号寻址的是一个由
/// 客户端或其他 Grain 调用的任务，该任务的执行树内任何位置的等待操作都能观察该任务的信号。
/// </para>
/// <para>
/// 每个名称在每个任务中只锁存一个值：宿主记录收到的第一个值，对该名称的所有等待操作
/// 都返回已记录的值，包括信号到达前开始的等待、到达后开始的等待，以及
/// 重放期间的等待。重复发送相等的信号值不会改变任务，发送不同值
/// 则会被拒绝。使用 <see cref="For(string)"/> 为重复事件的每次出现派生不同的名称。
/// </para>
/// </remarks>
public sealed class DurableSignal<T>
{
    /// <summary>初始化名为 <paramref name="name"/> 的信号描述符。</summary>
    /// <param name="name">信号名称，在同一任务的信号中必须唯一。</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空。</exception>
    public DurableSignal(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>获取信号名称。</summary>
    public string Name { get; }

    /// <summary>获取此信号一次出现的描述符，命名为 <c>{Name}/{suffix}</c>。</summary>
    /// <param name="suffix">此次出现的稳定标识符，例如审批轮次或行号。</param>
    /// <returns>具有派生名称和相同值类型的描述符。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="suffix"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="suffix"/> 为空。</exception>
    public DurableSignal<T> For(string suffix)
    {
        ArgumentException.ThrowIfNullOrEmpty(suffix);
        return new($"{Name}/{suffix}");
    }

    /// <summary>创建等待此信号并返回其值的定义。</summary>
    /// <returns>
    /// 一种定义，在持久化执行实例中运行时，以信号已记录的值完成。直接等待该定义时，
    /// 它作为等待方执行实例的一部分运行；作为 <see cref="DurableTask.WhenAny(IReadOnlyList{DurableTask})"/> 的输入时，
    /// 它与其他输入竞争，例如由 <see cref="DurableTask.DelayUntil(DateTimeOffset)"/> 实现的超时。
    /// </returns>
    /// <remarks>
    /// 等待方执行实例的持久化取消会取消等待，除非使用以下方法屏蔽保护该等待：
    /// <see cref="DurableTaskExtensions.Shielded{TResult}(DurableTask{TResult})"/>。受屏蔽保护的等待会接收
    /// 取消后到达的信号，只有中止才会结束它。信号值是发送方针对该
    /// 信号类型提供的值；值类型不同会使等待抛出 <see cref="InvalidCastException"/>。
    /// </remarks>
    public DurableTask<T> WaitAsync() => new SignalWaitDurableTask<T>(Name);

    /// <summary>创建等待此信号直到 <paramref name="deadline"/> 的定义。</summary>
    /// <param name="deadline">停止等待的时间，以宿主逻辑时间计量。</param>
    /// <returns>
    /// 此定义在持久化执行实例中被等待时，作为该执行实例的一部分运行，并在完成时返回
    /// <c>(true, value)</c>（当信号在与 <paramref name="deadline"/> 的竞争中获胜时），或
    /// <c>(false, default)</c>（当截止时间获胜时）。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 此定义是一个 <see cref="DurableTask.Race(IReadOnlyList{DurableTask})"/>，竞争双方为 <see cref="WaitAsync"/>（
    /// 输入 <c>0</c>）和 <see cref="DurableTask.DelayUntil(DateTimeOffset)"/>（输入 <c>1</c>），两者的标识符均位于
    /// 其操作标识符之下，该操作标识符是 <c>WithId</c> 分配的段，或按执行顺序生成的段。
    /// 获胜者是最先完成的输入，宿主在重放时选择相同的获胜者，因此重放返回
    /// 相同的结果。另一个输入随后收到持久化取消请求，请求携带原因
    /// <c>Another task won the race.</c>。两个输入均达到终态后，此定义才完成，因此等待与
    /// 截止时间的延迟都不会比该定义存活更久。
    /// </para>
    /// <para>
    /// 截止时间获胜后，信号仍保留其锁存机制：之后的 <see cref="WaitAsync"/> 会返回在截止时间
    /// 之后到达的值。等待方执行实例的持久化取消会取消竞争，除非使用
    /// <see cref="DurableTaskExtensions.Shielded{TResult}(DurableTask{TResult})"/> 对该竞争进行屏蔽保护，此时两个输入都受到保护。
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
