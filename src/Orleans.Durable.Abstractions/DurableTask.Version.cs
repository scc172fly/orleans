namespace Orleans.Durable.Tasks;

public abstract partial class DurableTask
{
    /// <summary>
    /// 返回正在运行的持久化任务所遵循的变更 <paramref name="name"/> 的版本，使代码能够
    /// 修改工作流时，仍能重放在修改之前启动的执行实例。
    /// </summary>
    /// <param name="name">
    /// 变更在运行中任务内的名称，例如 <c>"carrier-api"</c>。同一执行实例中每次使用相同名称的调用
    /// 都返回相同版本。名称不能包含 <c>$</c>。
    /// </param>
    /// <param name="minSupported">
    /// 调用代码仍支持的最早版本。版本 <c>0</c> 表示首次引入该变更
    /// 之前的代码。
    /// </param>
    /// <param name="maxSupported">最新版本；首次到达该调用的执行实例会记录此版本。</param>
    /// <returns>范围从 <paramref name="minSupported"/> 到 <paramref name="maxSupported"/> 的版本。</returns>
    /// <remarks>
    /// <para>
    /// 执行实例中首次使用 <paramref name="name"/> 的调用决定版本，并将其与执行实例一起记录，
    /// 每次后续调用及每次重放都返回已记录的版本：
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// 首次到达此调用的执行实例记录 <paramref name="maxSupported"/>。
    /// </description></item>
    /// <item><description>
    /// 如果某执行实例在先前部署中已经越过此位置（当时没有该调用），则记录 <c>0</c>：其重放
    /// 发现尚未到达的已记录操作，说明先前运行继续执行了变更前的代码。
    /// </description></item>
    /// <item><description>
    /// 步骤继承同一 Grain 上外围执行实例为以下变更记录的版本：
    /// <paramref name="name"/>.
    /// </description></item>
    /// </list>
    /// <para>
    /// 根据返回的版本分支，较新版本优先，并保留每个分支的操作和名称，直到
    /// 遵循该分支的待处理执行实例全部结束。记录会随下一次持久化写入提交，行为与
    /// <see cref="RunInline{TResult}(string, Func{TResult})"/> 效果的记录相同。
    /// </para>
    /// <para>
    /// 要淘汰旧分支，请在没有待处理执行实例遵循该版本后提高 <paramref name="minSupported"/>，
    /// 并移除分支。已记录版本不在支持范围内的执行实例将停滞：宿主会以
    /// <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/> 为原因取消执行尝试，因此调用会抛出
    /// <see cref="DurableTaskAttemptCanceledException"/>，其内部异常为
    /// <see cref="DurableTaskVersionException"/>；宿主丢弃执行尝试的结果，并使任务保持待处理状态，直到之后的
    /// 激活实例重放该任务。
    /// </para>
    /// <para>
    /// 在持久化任务之外，调用返回 <paramref name="maxSupported"/>，因此普通方法与持久化任务主体共享的辅助方法
    /// 也可以调用它。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var version = DurableTask.GetVersion("carrier-api", minSupported: 0, maxSupported: 1);
    /// if (version &gt;= 1)
    /// {
    ///     await carrier.BookAsync(order).WithId("book");
    /// }
    /// else
    /// {
    ///     await legacyShipping.ShipAsync(order).WithId("ship");
    /// }
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 为空或包含 <c>$</c>。</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minSupported"/> 为负值，或者 <paramref name="maxSupported"/> 小于
    /// <paramref name="minSupported"/>.
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 宿主取消了执行尝试，例如由于执行实例记录了支持范围以外的版本，
    /// 导致其停滞；此时异常的内部异常为 <see cref="DurableTaskVersionException"/>。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 宿主无法从调用代码所在位置记录版本，例如在 <c>ConfigureAwait(false)</c> 之后。
    /// </exception>
    /// <exception cref="NotSupportedException">宿主不支持版本控制。</exception>
    public static int GetVersion(string name, int minSupported, int maxSupported)
    {
        DurableTaskContext.ValidateName(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegative(minSupported);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSupported, minSupported);
        return DurableTaskContext.Ambient is { } context
            ? context.DecideVersion(name, minSupported, maxSupported)
            : maxSupported;
    }
}
