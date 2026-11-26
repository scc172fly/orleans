using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Runtime;

namespace Orleans.Durable.Tasks;

/// <summary>
/// 其他各方所完成的一项持久任务: grain 将任务和任务识别符存放在
/// 以 grain。
/// </summary>
/// <param name="Target">下 grain 保存此项任务。</param>
/// <param name="Id">上 <paramref name="Target"/>.</param>上的任务识别符号。
/// <remarks>
/// <para>
/// 地址是一个编码存储和发送的值: Orleans 以 grain 电话和日记状态将其序列化,
/// System.Text.Json 序列化为 <c>{"Target":"{grain type}/{grain key}","Id":"{task ID}"}</c>,所以 JSON 日记和
/// HTTP有效载荷未经登记即携带。
/// <see cref="DurableTaskAddressExtensions.GetDurableTask{TResult}(IGrainFactory, DurableTaskAddress)"/> 转回去,
/// 正在等待、 民意调查、 取消或中止任务的引用 。
/// </para>
/// <para>
/// 地址命名为 grain 留给其他各方的任务:根任务,即客户、 grain或耐用
/// 开始执行执行, 包括一个独立任务, 或者客户端或其他 grain 援引的A grain 保留a
/// <c>DurableTaskOptions.ResultRetentionPeriod</c>的终端根任务,之后进行搜索
/// <see cref="DurableTaskNotFoundException"/>.
/// </para>
/// </remarks>
[GenerateSerializer, Immutable]
[Alias("Orleans.Durable.Tasks.DurableTaskAddress")]
public readonly record struct DurableTaskAddress(
    [property: Id(0)] GrainId Target,
    [property: Id(1)] TaskId Id) : IDurableTaskReference;

/// <summary>创建和解决持久任务的地址。</summary>
public static class DurableTaskAddressExtensions
{
    /// <summary>查找长期任务 <paramref name="address"/>也没有安排好</summary>
    /// <typeparam name="TResult">持久任务结果类型。</typeparam>
    /// <param name="grainFactory"> grain 工厂,如 grain的 <c>GrainFactory</c> 或 <see cref="IClusterClient"/>。</param>
    /// <param name="address">任务地址</param>
    /// <returns>
    /// 以
    /// <see cref="DurableTaskGrainExtensions.GetDurableTask{TResult}(IAddressable, TaskId)"/> 描述。
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="grainFactory"/> 是 <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="address"/> 没有无 grain 或无任务标识符。</exception>
    public static DurableTaskReference<TResult> GetDurableTask<TResult>(this IGrainFactory grainFactory, DurableTaskAddress address)
    {
        ArgumentNullException.ThrowIfNull(grainFactory);
        if (address.Target.IsDefault || address.Id.IsDefault)
        {
            throw new ArgumentException("A durable task address requires a grain and a task identifier.", nameof(address));
        }

        return grainFactory.GetGrain(address.Target).GetDurableTask<TResult>(address.Id);
    }

    /// <summary>获取任务地址 <paramref name="task"/> 观察着它,当它到达其他当事方。</summary>
    /// <param name="task">控点。</param>
    /// <param name="address">任务地址,当该方法返回 <see langword="true"/>时。</param>
    /// <returns>
    /// <see langword="true"/> 当手柄是被取而专取之的时候, grain 留留给其他缔约方:根任务
    /// 客户端或普通 grain 方法开始,或者在持久执行中,一项独立任务或一项任务
    /// <c>GetDurableTask</c> 被处决者树所拥有附着的孩子没有地址。
    /// </returns>
    /// <remarks>地址的识别符号是控点的 <see cref="ScheduledTask.TargetTaskId"/>。</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> 是 <see langword="null"/>.</exception>
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
