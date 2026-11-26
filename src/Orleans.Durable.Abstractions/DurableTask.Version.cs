namespace Orleans.Durable.Tasks;

public abstract partial class DurableTask
{
    /// <summary>
    /// 返回运行中的耐久任务所遵循的修改 <paramref name="name"/> 版本,所以代码
    /// 修改工作流程时会不断重现在修改前开始的处决。
    /// </summary>
    /// <param name="name">
    /// 运行中任务中更改的名称, 例如 <c>"carrier-api"</c>. Every call with the same name in one
    /// 执行返回同一版本。名称不包含 <c>$</c>。
    /// </param>
    /// <param name="minSupported">
    /// 调用代码仍然支持的最老版本。 版本 <c>0</c> 是第一个更改前的代码
    /// 版本。
    /// </param>
    /// <param name="maxSupported">最新版本,第一次被呼吁执行的处决首次得到记录。</param>
    /// <returns>从 <paramref name="minSupported"/> 到 <paramref name="maxSupported"/>的版本。</returns>
    /// <remarks>
    /// <para>
    /// 执行中的第一次 <paramref name="name"/> 呼叫决定版本,并记录其执行情况,
    /// 每一后一呼声 每回播放一次 都返回了录音版本
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// 首次达到调用记录的执行 <paramref name="maxSupported"/>。
    /// </description></item>
    /// <item><description>
    /// 执行中,如果先前部署的一执行在未调用的情况下经过此点,记录 <c>0</c>:它的重放
    /// 发现尚未达到的操作, 所以在更改前早期运行继续使用代码。
    /// </description></item>
    /// <item><description>
    /// 步骤继承版本,其中附有在记录同一 grain 的
    /// <paramref name="name"/>.
    /// </description></item>
    /// </list>
    /// <para>
    /// 将每个处的业务和名称保留在处决期间
    /// 记录将用下封的永久写作来记录,作为
    /// <see cref="RunInline{TResult}(string, Func{TResult})"/> 效果是。
    /// </para>
    /// <para>
    /// 退休、调 <paramref name="minSupported"/> 和在无待执行时撤职
    /// 执行时,如果其记录版本在支持的距离摊位之外:主机取消
    /// 企图或企图 <see cref="DurableTaskAttemptCancellationReason.UnsupportedVersion"/>,所以呼声扔
    /// <see cref="DurableTaskAttemptCanceledException"/> 其内心的例外,是
    /// <see cref="DurableTaskVersionException"/>,放弃尝试的结果,将任务留待日后完成
    /// 激活重播它。
    /// </para>
    /// <para>
    /// 长期任务之外的一个外部任务,调用返回 <paramref name="maxSupported"/>,因此普通方法共享的帮助者
    /// 由长期任务机构组成的工作组可以称之为。
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
    /// <exception cref="ArgumentNullException"><paramref name="name"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> 是空或包含 <c>$</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="minSupported"/> 是负,或 <paramref name="maxSupported"/> 小于
    /// <paramref name="minSupported"/>.
    /// </exception>
    /// <exception cref="DurableTaskAttemptCanceledException">
    /// 执行失败后主机取消了尝试, 例如因为执行过程记录了一个超出支持范围之外的版本,
    /// 以拖住它;例外的内部例外是 <see cref="DurableTaskVersionException"/>。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 主机无法从调用代码中记录版本, 例如在 <c>ConfigureAwait(false)</c>之后 。
    /// </exception>
    /// <exception cref="NotSupportedException">主机不支持版本。</exception>
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
