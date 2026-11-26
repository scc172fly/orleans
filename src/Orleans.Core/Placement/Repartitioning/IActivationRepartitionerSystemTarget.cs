using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Orleans.Runtime;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System;

namespace Orleans.Placement.Repartitioning;

/// <summary>
/// 定义用于协调 silos之间启动交换的系统目标合同。
/// </summary>
[Alias("IActivationRepartitionerSystemTarget")]
internal interface IActivationRepartitionerSystemTarget : ISystemTarget
{
    /// <summary>
    /// 在指定 silo上获取激活再分配系统目标的引用。
    /// </summary>
    /// <param name="grainFactory">下 grain 用于创建系统目标参考的工厂。</param>
    /// <param name="targetSilo">目标激活重新分割器所在的 silo 。</param>
    /// <returns>引用激活重新分配系统目标 <paramref name="targetSilo"/>.</returns>
    static IActivationRepartitionerSystemTarget GetReference(IGrainFactory grainFactory, SiloAddress targetSilo)
        => grainFactory.GetGrain<IActivationRepartitionerSystemTarget>(SystemTargetGrainId.Create(Constants.ActivationRepartitionerType, targetSilo).GrainId);

    /// <summary>
    /// 提议启动另一个活动 silo的交换, 以此开始重新分配回合 。
    /// </summary>
    /// <returns>代表交换尝试的价值任务。</returns>
    [ResponseTimeout("00:10:00")]
    [Alias("A6EE4757")]
    ValueTask TriggerExchangeRequest(CancellationToken cancellationToken = default);

    /// <summary>
    /// 评估另一个 silo提议的启动交换。
    /// </summary>
    /// <param name="request">拟议的启动交换。</param>
    /// <returns>说明交换所是否被接受以及哪些交易所激活的答复 silo 将转移。</returns>
    [ResponseTimeout("00:10:00")]
    [Alias("9D8EDC44")]
    ValueTask<AcceptExchangeResponse> AcceptExchangeRequest(AcceptExchangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置所收集的 grain- Call 统计。此方法支持测试 。
    /// </summary>
    /// <param name="cancellationToken">取消操作所用的标记。</param>
    /// <returns>代表重置操作的值任务。</returns>
    [Alias("21852A09")]
    ValueTask ResetCounters(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 silo上的当前激活次数。此方法支持测试。
    /// </summary>
    /// <param name="cancellationToken">取消操作所用的标记。</param>
    /// <returns>当前的激活次数 silo.</returns>
    [Alias("9FB525F3")]
    ValueTask<int> GetActivationCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置一个偏移, 将其添加到当前激活计数中。 此方法支持测试 。
    /// </summary>
    /// <param name="activationCountOffset">在当前激活数中添加的偏移。</param>
    /// <param name="cancellationToken">取消操作所用的标记。</param>
    /// <returns>代表更新操作的值任务。</returns>
    [Alias("135356E5")]
    ValueTask SetActivationCountOffset(int activationCountOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 grain-呼叫在 silo上收集的频率。
    /// </summary>
    /// <param name="cancellationToken">取消操作所用的标记。</param>
    /// <returns>通讯边缘和他们的估计通话次数</returns>
    [Alias("C4497899")]
    ValueTask<ImmutableArray<(Edge, ulong)>> GetGrainCallFrequencies(CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待所有缓冲信件从输入缓冲区中移除。 此方法支持测试 。
    /// </summary>
    /// <param name="cancellationToken">取消操作所用的标记。</param>
    /// <returns>代表冲洗操作的值任务。</returns>
    [Alias("11731652")]
    ValueTask FlushBuffers(CancellationToken cancellationToken = default);
}

// 我们使用只读的支架,这样我们就可以完全分离信息传递和可能修改 Silo 字段。
/// <summary>
/// 代表源和目标之间的“ 通信边缘” 的数据结构。
/// </summary>
/// <param name="source">源头顶端。</param>
/// <param name="target">目标顶部。</param>
[GenerateSerializer, Immutable, DebuggerDisplay("Source: [{Source.Id} - {Source.Silo}] | Target: [{Target.Id} - {Target.Silo}]")]
internal readonly struct Edge(EdgeVertex source, EdgeVertex target) : IEquatable<Edge>
{
    /// <summary>
    /// 获取源头顶部。
    /// </summary>
    [Id(0)]
    public EdgeVertex Source { get; } = source;

    /// <summary>
    /// 获得目标顶部。
    /// </summary>
    [Id(1)]
    public EdgeVertex Target { get; } = target;

    /// <summary>
    /// 确定两个边缘是否相等。
    /// </summary>
    /// <param name="left">比较的第一个边缘。</param>
    /// <param name="right">比较的第二边缘。</param>
    /// <returns><see langword="true"/> 如果边缘相等;否则, <see langword="false"/>。</returns>
    public static bool operator ==(Edge left, Edge right) => left.Equals(right);

    /// <summary>
    /// 确定两个边缘是否相等。
    /// </summary>
    /// <param name="left">比较的第一个边缘。</param>
    /// <param name="right">比较的第二边缘。</param>
    /// <returns><see langword="true"/> ,如果边缘不相等;否则, <see langword="false"/>。</returns>
    public static bool operator !=(Edge left, Edge right) => !left.Equals(right);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Edge other && Equals(other);

    /// <inheritdoc />
    public bool Equals(Edge other) => Source == other.Source && Target == other.Target;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Source, Target);

    /// <summary>
    /// 返回副本, 但含有翻转源和目标 。
    /// </summary>
    /// <returns>边缘,其源和目标被逆转。</returns>
    public Edge Flip() => new(source: Target, target: Source);

    /// <inheritdoc />
    public override string ToString() => $"[{Source} -> {Target}]";
}

/// <summary>
/// 数据结构代表 <see cref="Edge"/>的一面。
/// </summary>
/// <param name="id"> grain 标识符。</param>
/// <param name="silo">下 silo 位于 grain 启动行动</param>
/// <param name="isMigratable">激活能否迁移。</param>
[GenerateSerializer, Immutable]
public readonly struct EdgeVertex(
    GrainId id,
    SiloAddress silo,
    bool isMigratable) : IEquatable<EdgeVertex>
{
    /// <summary>
    /// grain 标识符。
    /// </summary>
    [Id(0)]
    public readonly GrainId Id = id;

    /// <summary>
    /// 下 silo 位于 grain 启动行动
    /// </summary>
    [Id(1)]
    public readonly SiloAddress Silo = silo;

    /// <summary>
    /// 显示激活能否迁移的值 。
    /// </summary>
    [Id(2)]
    public readonly bool IsMigratable = isMigratable;

    /// <summary>
    /// 确定两个顶点是否相等。
    /// </summary>
    /// <param name="left">第一个要比较的顶点。</param>
    /// <param name="right">比较的第二个顶点。</param>
    /// <returns><see langword="true"/> ,如果顶点相等;否则, <see langword="false"/>。</returns>
    public static bool operator ==(EdgeVertex left, EdgeVertex right) => left.Equals(right);

    /// <summary>
    /// 确定两个顶点是否相等。
    /// </summary>
    /// <param name="left">第一个要比较的顶点。</param>
    /// <param name="right">比较的第二个顶点。</param>
    /// <returns><see langword="true"/> 如果顶点不相等;否则, <see langword="false"/>。</returns>
    public static bool operator !=(EdgeVertex left, EdgeVertex right) => !left.Equals(right);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EdgeVertex other && Equals(other);

    /// <inheritdoc />
    public bool Equals(EdgeVertex other) => Id == other.Id && Silo.Equals(other.Silo) && IsMigratable == other.IsMigratable;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Silo, IsMigratable);

    /// <inheritdoc />
    public override string ToString() => $"[{Id}@{Silo}{(IsMigratable ? "" : "/NotMigratable")}]";
}

/// <summary>
/// 将转到另一个 silo的候选人的顶点。
/// </summary>
[GenerateSerializer, DebuggerDisplay("Id = {Id} | Accumulated = {AccumulatedTransferScore}")]
internal sealed class CandidateVertex
{
    /// <summary>
    /// 候选人的身份 grain。
    /// </summary>
    [Id(0), Immutable]
    public GrainId Id { get; init; }

    /// <summary>
    /// 将顶端与 <see cref="Id"/> 迁移到另一个 silo时预期的成本减少。
    /// </summary>
    [Id(1)]
    public long AccumulatedTransferScore { get; set; }

    /// <summary>
    /// 这些是连接到 <see cref="Id"/>顶点的所有顶点。
    /// </summary>
    /// <remarks>当这个顶点从接收器上最大排列的堆积中移走时,这些将是非常重要的 silo.</remarks>
    [Id(2), Immutable]
    public ImmutableArray<CandidateConnectedVertex> ConnectedVertices { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => $"[{Id} * {AccumulatedTransferScore} -> [{string.Join(", ", ConnectedVertices)}]]";
}

/// <summary>
/// 代表与一名启动的移徙候选人有关的顶点及其对转移分的贡献。
/// </summary>
/// <param name="id">连接的 grain 标识符。</param>
/// <param name="transferScore">连接所贡献的传输分数。</param>
[GenerateSerializer, Immutable]
public readonly struct CandidateConnectedVertex(GrainId id, long transferScore)
{
    /// <summary>
    /// 获取连接的 grain 标识符。
    /// </summary>
    [Id(0)]
    public GrainId Id { get; } = id;

    /// <summary>
    /// 获得连接带来的传输分数。
    /// </summary>
    [Id(1)]
    public long TransferScore { get; } = transferScore;

    /// <summary>
    /// 确定两个相连接的候选人顶点是否相等。
    /// </summary>
    /// <param name="left">第一个要比较的连接顶点。</param>
    /// <param name="right">第二个连接的顶点要比较。</param>
    /// <returns><see langword="true"/> 如果连接的顶点相等;否则, <see langword="false"/>。</returns>
    public static bool operator ==(CandidateConnectedVertex left, CandidateConnectedVertex right) => left.Equals(right);

    /// <summary>
    /// 确定两个连接的候选人顶点是否相等。
    /// </summary>
    /// <param name="left">第一个要比较的连接顶点。</param>
    /// <param name="right">第二个连接的顶点要比较。</param>
    /// <returns><see langword="true"/> 如果连接的顶点不相等;否则, <see langword="false"/>。</returns>
    public static bool operator !=(CandidateConnectedVertex left, CandidateConnectedVertex right) => !left.Equals(right);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CandidateConnectedVertex other && Equals(other);

    /// <summary>
    /// 确定此实例是否等于其他关联的候选人顶点。
    /// </summary>
    /// <param name="other">与此实例相比的相连接的候选人顶点。</param>
    /// <returns><see langword="true"/> ,如果情况相同;否则, <see langword="false"/>。</returns>
    public bool Equals(CandidateConnectedVertex other) => Id == other.Id && TransferScore == other.TransferScore;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, TransferScore);

    /// <inheritdoc />
    public override string ToString() => $"[{Id} * {TransferScore}]";
}

/// <summary>
/// 代表另一个 silo的拟议启动交换。
/// </summary>
/// <param name="sendingSilo">下 silo 提议交换。</param>
/// <param name="exchangeSet">发送时的激活 silo 拟议的转让。</param>
/// <param name="activationCountSnapshot">请求创建时发送 silo的激活计数。</param>
[GenerateSerializer, Immutable]
internal sealed class AcceptExchangeRequest(SiloAddress sendingSilo, ImmutableArray<CandidateVertex> exchangeSet, int activationCountSnapshot)
{
    /// <summary>
    /// 下 silo 提出转让的 grains 对我们
    /// </summary>
    [Id(0)]
    public SiloAddress SendingSilo { get; } = sendingSilo;

    /// <summary>
    /// 集 grains 发送 silo 准备转移给我们
    /// </summary>
    [Id(1)]
    public ImmutableArray<CandidateVertex> ExchangeSet { get; } = exchangeSet;

    /// <summary>
    /// 交换请求时发送 silo 的激活计数。
    /// </summary>
    [Id(2)]
    public int ActivationCountSnapshot { get; } = activationCountSnapshot;
}

/// <summary>
/// 系对拟议启动交换所进行评价的结果。
/// </summary>
/// <param name="type">交换要求的结果。</param>
/// <param name="acceptedGrains">下 grains 接受请求方接受的请求 silo.</param>
/// <param name="givenGrains">下 grains 向请求国提供 silo.</param>
[GenerateSerializer, Immutable]
internal sealed class AcceptExchangeResponse(AcceptExchangeResponse.ResponseType type, ImmutableArray<GrainId> acceptedGrains, ImmutableArray<GrainId> givenGrains)
{
    /// <summary>
    /// 获取缓存回复, 显示接收方 silo 最近刚刚完成另一个交换。
    /// </summary>
    public static readonly AcceptExchangeResponse CachedExchangedRecently = new(ResponseType.ExchangedRecently, [], []);

    /// <summary>
    /// 得到一个缓存回复,表示两个 silos 都试图启动同一交换。
    /// </summary>
    public static readonly AcceptExchangeResponse CachedMutualExchangeAttempt = new(ResponseType.MutualExchangeAttempt, [], []);

    /// <summary>
    /// 得到交换要求的结果。
    /// </summary>
    [Id(0)]
    public ResponseType Type { get; } = type;

    /// <summary>
    /// 发送者要求接收人转让的 grains 。
    /// </summary>
    [Id(1)]
    public ImmutableArray<GrainId> AcceptedGrainIds { get; } = acceptedGrains;

    /// <summary>
    /// 接收器向发送者传送的 grains 。
    /// </summary>
    [Id(2)]
    public ImmutableArray<GrainId> GivenGrainIds { get; } = givenGrains;

    /// <summary>
    /// 描述启动交换请求的结果。
    /// </summary>
    [GenerateSerializer]
    public enum ResponseType
    {
        /// <summary>
        /// 接受交换,并退还了一套交换设备。
        /// </summary>
        Success,

        /// <summary>
        /// 另一 silo 最近又参与了另一场交易。
        /// </summary>
        ExchangedRecently,

        /// <summary>
        /// 试图同时进行这一类与另一类 silo 的交流。
        /// </summary>
        MutualExchangeAttempt
    }
}
