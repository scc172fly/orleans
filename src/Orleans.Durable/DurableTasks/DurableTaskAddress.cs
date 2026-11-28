using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 其他参与方访问持久化任务时所用的地址：存储任务的 Grain 以及任务在
/// 该 Grain 上的标识符。
/// </summary>
/// <param name="Target">存储任务的 Grain。</param>
/// <param name="Id">任务在 <paramref name="Target"/> 上的标识符。</param>
/// <remarks>
/// <para>
/// 地址是代码可存储和发送的值：Orleans 会在 Grain 调用和日志化状态中序列化它，
/// System.Text.Json 将其序列化为 <c>{"Target":"{grain type}/{grain key}","Id":"{task ID}"}</c>，因此 JSON 日志及
/// HTTP 有效载荷无需注册即可携带它。
/// <see cref="DurableTaskAddressExtensions.GetDurableTask{TResult}(IGrainFactory, DurableTaskAddress)"/> 将其转换回
/// 引用，可用于等待、轮询、取消或中止任务。
/// </para>
/// <para>
/// 地址标识 Grain 为其他参与方保留的任务：根任务，由客户端、Grain 或持久化
/// 执行启动的任务（包括分离任务），或由客户端或另一个 Grain 调用的任务。Grain 将
/// 终态根任务保留 <c>DurableTaskOptions.ResultRetentionPeriod</c> 指定的时长，之后查找会抛出
/// <see cref="DurableTaskNotFoundException"/>.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Tasks.DurableTaskAddress")]
public readonly record struct DurableTaskAddress(
    [property: Id(0)] GrainId Target,
    [property: Id(1)] TaskId Id) : IDurableTaskReference;

/// <summary>创建和解析持久化任务的地址。</summary>
public static class DurableTaskAddressExtensions
{
    /// <summary>获取 <paramref name="address"/> 处持久化任务的引用，而不调度该任务。</summary>
    /// <typeparam name="TResult">持久化任务的结果类型。</typeparam>
    /// <param name="grainFactory">Grain 工厂，例如 Grain 的 <c>GrainFactory</c> 或 <see cref="IClusterClient"/>。</param>
    /// <param name="address">任务的地址。</param>
    /// <returns>
    /// 一个具有如下行为的引用：
    /// <see cref="DurableTaskGrainExtensions.GetDurableTask{TResult}(IAddressable, TaskId)"/> 所描述的行为。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="grainFactory"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="ArgumentException"><paramref name="address"/> 未指定 Grain 或任务标识符。</exception>
    public static DurableTaskReference<TResult> GetDurableTask<TResult>(this IGrainFactory grainFactory, DurableTaskAddress address)
    {
        ArgumentNullException.ThrowIfNull(grainFactory);
        if (address.Target.IsDefault || address.Id.IsDefault)
        {
            throw new ArgumentException("A durable task address requires a grain and a task identifier.", nameof(address));
        }

        return grainFactory.GetGrain(address.Target).GetDurableTask<TResult>(address.Id);
    }

    /// <summary>当其他参与方可访问任务时，获取 <paramref name="task"/> 所观察任务的地址。</summary>
    /// <param name="task">句柄。</param>
    /// <param name="address">本方法返回 <see langword="true"/> 时的任务地址。</param>
    /// <returns>
    /// <see langword="true"/> 表示句柄对应的任务属于其 Grain 为其他参与方保留的任务：由
    /// 客户端或普通 Grain 方法启动的根任务，或者在持久化执行中启动的分离任务或
    /// <c>GetDurableTask</c> 所观察的任务。附接子任务由执行实例的任务树拥有，没有地址。
    /// </returns>
    /// <remarks>地址的标识符为句柄的 <see cref="ScheduledTask.TargetTaskId"/>。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 为 <see langword="null"/>。</exception>
    public static bool TryGetAddress(this ScheduledTask task, out DurableTaskAddress address)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.GetFeature<IDurableTaskReference>() is { } reference)
        {
            address = new(reference.Target, reference.Id);
            return true;
        }

        address = default;
        return false;
    }
}
