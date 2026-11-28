using System.Threading;
using System.Threading.Tasks;
using System.Collections.Immutable;
using Orleans.Runtime;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System;

namespace Orleans.Placement.Repartitioning;

/// <summary>
/// 定义用于协调 Silo 之间激活实例交换的系统目标契约。
/// </summary>
[Alias("IActivationRepartitionerSystemTarget")]
internal interface IActivationRepartitionerSystemTarget : ISystemTarget
{
    /// <summary>
    /// 获取对指定 Silo 上激活实例重新分区器系统目标的引用。
    /// </summary>
    /// <param name="grainFactory">用于创建系统目标引用的 Grain 工厂。</param>
    /// <param name="targetSilo">承载目标激活实例重新分区器的 Silo。</param>
    /// <returns>对 <paramref name="targetSilo"/> 上的激活实例重新分区器系统目标的引用。</returns>
    static IActivationRepartitionerSystemTarget GetReference(IGrainFactory grainFactory, SiloAddress targetSilo)
        => grainFactory.GetGrain<IActivationRepartitionerSystemTarget>(SystemTargetGrainId.Create(Constants.ActivationRepartitionerType, targetSilo).GrainId);

    /// <summary>
    /// 向另一个活动 Silo 提议交换激活实例，从而开始一轮重新分区。
    /// </summary>
    /// <returns>表示此次交换尝试的 ValueTask。</returns>
    [ResponseTimeout("00:10:00")]
    [Alias("A6EE4757")]
    ValueTask TriggerExchangeRequest(CancellationToken cancellationToken = default);

    /// <summary>
    /// 评估另一 Silo 提议的激活实例交换。
    /// </summary>
    /// <param name="request">提议的激活实例交换。</param>
    /// <returns>描述交换是否被接受以及各 Silo 将迁移哪些激活实例的响应。</returns>
    [ResponseTimeout("00:10:00")]
    [Alias("9D8EDC44")]
    ValueTask<AcceptExchangeResponse> AcceptExchangeRequest(AcceptExchangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 重置已收集的 Grain 调用统计信息。此方法用于支持测试。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示重置操作的 ValueTask。</returns>
    [Alias("21852A09")]
    ValueTask ResetCounters(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Silo 上当前的激活实例数量。此方法用于支持测试。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>Silo 上当前的激活实例数量。</returns>
    [Alias("9FB525F3")]
    ValueTask<int> GetActivationCount(CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置要加到当前激活实例计数上的偏移量。此方法用于支持测试。
    /// </summary>
    /// <param name="activationCountOffset">要加到当前激活实例计数上的偏移量。</param>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示更新操作的 ValueTask。</returns>
    [Alias("135356E5")]
    ValueTask SetActivationCountOffset(int activationCountOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取 Silo 上收集的 Grain 调用频率估计值。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>通信边及其估计调用次数。</returns>
    [Alias("C4497899")]
    ValueTask<ImmutableArray<(Edge, ulong)>> GetGrainCallFrequencies(CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待输入缓冲区中的所有已缓冲消息都被移除。此方法用于支持测试。
    /// </summary>
    /// <param name="cancellationToken">用于取消该操作的令牌。</param>
    /// <returns>表示缓冲区排空操作的 ValueTask。</returns>
    [Alias("11731652")]
    ValueTask FlushBuffers(CancellationToken cancellationToken = default);
}

// 使用只读结构体，以便将消息传递与对 Silo 字段可能发生的修改完全解耦。
/// <summary>
/// 表示源和目标之间“通信边”的数据结构。
/// </summary>
/// <param name="source">源顶点。</param>
/// <param name="target">目标顶点。</param>
[GenerateSerializer, Immutable, DebuggerDisplay("Source: [{Source.Id} - {Source.Silo}] | Target: [{Target.Id} - {Target.Silo}]")]
internal readonly struct Edge(EdgeVertex source, EdgeVertex target) : IEquatable<Edge>
{
    /// <summary>
    /// 获取源顶点。
    /// </summary>
    [Id(0)]
    public EdgeVertex Source { get; } = source;

    /// <summary>
    /// 获取目标顶点。
    /// </summary>
    [Id(1)]
    public EdgeVertex Target { get; } = target;

    /// <summary>
    /// 确定两条边是否相等。
    /// </summary>
    /// <param name="left">要比较的第一条边。</param>
    /// <param name="right">要比较的第二条边。</param>
    /// <returns><see langword="true"/> 表示两条边相等；否则为 <see langword="false"/>。</returns>
    public static bool operator ==(Edge left, Edge right) => left.Equals(right);

    /// <summary>
    /// 确定两条边是否不相等。
    /// </summary>
    /// <param name="left">要比较的第一条边。</param>
    /// <param name="right">要比较的第二条边。</param>
    /// <returns><see langword="true"/> 表示两条边不相等；否则为 <see langword="false"/>。</returns>
    public static bool operator !=(Edge left, Edge right) => !left.Equals(right);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Edge other && Equals(other);

    /// <inheritdoc />
    public bool Equals(Edge other) => Source == other.Source && Target == other.Target;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Source, Target);

    /// <summary>
    /// 返回交换源与目标后的副本。
    /// </summary>
    /// <returns>源与目标已对调的边。</returns>
    public Edge Flip() => new(source: Target, target: Source);

    /// <inheritdoc />
    public override string ToString() => $"[{Source} -> {Target}]";
}

/// <summary>
/// 表示 <see cref="Edge"/> 的一端的数据结构。
/// </summary>
/// <param name="id">Grain 标识符。</param>
/// <param name="silo">Grain 激活实例所在的 Silo。</param>
/// <param name="isMigratable">激活实例是否可以迁移。</param>
[GenerateSerializer, Immutable]
public readonly struct EdgeVertex(
    GrainId id,
    SiloAddress silo,
    bool isMigratable) : IEquatable<EdgeVertex>
{
    /// <summary>
    /// Grain 标识符。
    /// </summary>
    [Id(0)]
    public readonly GrainId Id = id;

    /// <summary>
    /// Grain 激活实例所在的 Silo。
    /// </summary>
    [Id(1)]
    public readonly SiloAddress Silo = silo;

    /// <summary>
    /// 指示激活实例是否可以迁移的值。
    /// </summary>
    [Id(2)]
    public readonly bool IsMigratable = isMigratable;

    /// <summary>
    /// 确定两个顶点是否相等。
    /// </summary>
    /// <param name="left">要比较的第一个顶点。</param>
    /// <param name="right">要比较的第二个顶点。</param>
    /// <returns><see langword="true"/> 表示两个顶点相等；否则为 <see langword="false"/>。</returns>
    public static bool operator ==(EdgeVertex left, EdgeVertex right) => left.Equals(right);

    /// <summary>
    /// 确定两个顶点是否不相等。
    /// </summary>
    /// <param name="left">要比较的第一个顶点。</param>
    /// <param name="right">要比较的第二个顶点。</param>
    /// <returns><see langword="true"/> 表示两个顶点不相等；否则为 <see langword="false"/>。</returns>
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
/// 计划迁移到另一 Silo 的候选顶点。
/// </summary>
[GenerateSerializer, DebuggerDisplay("Id = {Id} | Accumulated = {AccumulatedTransferScore}")]
internal sealed class CandidateVertex
{
    /// <summary>
    /// 候选 Grain 的 ID。
    /// </summary>
    [Id(0), Immutable]
    public GrainId Id { get; init; }

    /// <summary>
    /// 将具有 <see cref="Id"/> 的顶点迁移到另一 Silo 时预计减少的成本。
    /// </summary>
    [Id(1)]
    public long AccumulatedTransferScore { get; set; }

    /// <summary>
    /// 与具有 <see cref="Id"/> 的顶点相连的所有顶点。
    /// </summary>
    /// <remarks>从接收方 Silo 的最大堆中移除此顶点时，这些顶点十分重要。</remarks>
    [Id(2), Immutable]
    public ImmutableArray<CandidateConnectedVertex> ConnectedVertices { get; init; } = [];

    /// <inheritdoc />
    public override string ToString() => $"[{Id} * {AccumulatedTransferScore} -> [{string.Join(", ", ConnectedVertices)}]]";
}

/// <summary>
/// 表示与激活实例迁移候选项相连的顶点及其对迁移评分的贡献。
/// </summary>
/// <param name="id">相连 Grain 的标识符。</param>
/// <param name="transferScore">该连接对迁移评分的贡献。</param>
[GenerateSerializer, Immutable]
public readonly struct CandidateConnectedVertex(GrainId id, long transferScore)
{
    /// <summary>
    /// 获取相连 Grain 的标识符。
    /// </summary>
    [Id(0)]
    public GrainId Id { get; } = id;

    /// <summary>
    /// 获取该连接对迁移评分的贡献。
    /// </summary>
    [Id(1)]
    public long TransferScore { get; } = transferScore;

    /// <summary>
    /// 确定两个相连的候选顶点是否相等。
    /// </summary>
    /// <param name="left">要比较的第一个相连顶点。</param>
    /// <param name="right">要比较的第二个相连顶点。</param>
    /// <returns><see langword="true"/> 表示两个相连顶点相等；否则为 <see langword="false"/>。</returns>
    public static bool operator ==(CandidateConnectedVertex left, CandidateConnectedVertex right) => left.Equals(right);

    /// <summary>
    /// 确定两个相连的候选顶点是否不相等。
    /// </summary>
    /// <param name="left">要比较的第一个相连顶点。</param>
    /// <param name="right">要比较的第二个相连顶点。</param>
    /// <returns><see langword="true"/> 表示两个相连顶点不相等；否则为 <see langword="false"/>。</returns>
    public static bool operator !=(CandidateConnectedVertex left, CandidateConnectedVertex right) => !left.Equals(right);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is CandidateConnectedVertex other && Equals(other);

    /// <summary>
    /// 确定此实例是否与另一个相连的候选顶点相等。
    /// </summary>
    /// <param name="other">要与此实例比较的相连候选顶点。</param>
    /// <returns><see langword="true"/> 表示两个实例相等；否则为 <see langword="false"/>。</returns>
    public bool Equals(CandidateConnectedVertex other) => Id == other.Id && TransferScore == other.TransferScore;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, TransferScore);

    /// <inheritdoc />
    public override string ToString() => $"[{Id} * {TransferScore}]";
}

/// <summary>
/// 表示来自另一 Silo 的激活实例交换提议。
/// </summary>
/// <param name="sendingSilo">提出交换的 Silo。</param>
/// <param name="exchangeSet">发送方 Silo 提议迁移的激活实例。</param>
/// <param name="activationCountSnapshot">创建请求时发送方 Silo 的激活实例计数。</param>
[GenerateSerializer, Immutable]
internal sealed class AcceptExchangeRequest(SiloAddress sendingSilo, ImmutableArray<CandidateVertex> exchangeSet, int activationCountSnapshot)
{
    /// <summary>
    /// 提议向本 Silo 迁移 Grain 的 Silo。
    /// </summary>
    [Id(0)]
    public SiloAddress SendingSilo { get; } = sendingSilo;

    /// <summary>
    /// 发送方 Silo 提议迁移到本 Silo 的 Grain 集合。
    /// </summary>
    [Id(1)]
    public ImmutableArray<CandidateVertex> ExchangeSet { get; } = exchangeSet;

    /// <summary>
    /// 发出交换请求时发送方 Silo 的激活实例计数。
    /// </summary>
    [Id(2)]
    public int ActivationCountSnapshot { get; } = activationCountSnapshot;
}

/// <summary>
/// 表示对激活实例交换提议进行评估的结果。
/// </summary>
/// <param name="type">交换请求的结果。</param>
/// <param name="acceptedGrains">接受自请求方 Silo 的 Grain。</param>
/// <param name="givenGrains">提议迁移给请求方 Silo 的 Grain。</param>
[GenerateSerializer, Immutable]
internal sealed class AcceptExchangeResponse(AcceptExchangeResponse.ResponseType type, ImmutableArray<GrainId> acceptedGrains, ImmutableArray<GrainId> givenGrains)
{
    /// <summary>
    /// 获取缓存的响应，指示接收方 Silo 刚完成另一场交换，间隔时间过短。
    /// </summary>
    public static readonly AcceptExchangeResponse CachedExchangedRecently = new(ResponseType.ExchangedRecently, [], []);

    /// <summary>
    /// 获取缓存的响应，指示两个 Silo 都尝试发起同一场交换。
    /// </summary>
    public static readonly AcceptExchangeResponse CachedMutualExchangeAttempt = new(ResponseType.MutualExchangeAttempt, [], []);

    /// <summary>
    /// 获取交换请求的结果。
    /// </summary>
    [Id(0)]
    public ResponseType Type { get; } = type;

    /// <summary>
    /// 发送方要求接收方迁移的 Grain。
    /// </summary>
    [Id(1)]
    public ImmutableArray<GrainId> AcceptedGrainIds { get; } = acceptedGrains;

    /// <summary>
    /// 接收方将向发送方迁移的 Grain。
    /// </summary>
    [Id(2)]
    public ImmutableArray<GrainId> GivenGrainIds { get; } = givenGrains;

    /// <summary>
    /// 描述激活实例交换请求的结果。
    /// </summary>
    [GenerateSerializer]
    public enum ResponseType
    {
        /// <summary>
        /// 交换已被接受，并返回交换集合。
        /// </summary>
        Success,

        /// <summary>
        /// 另一 Silo 最近参与过另一场交换。
        /// </summary>
        ExchangedRecently,

        /// <summary>
        /// 本 Silo 和另一 Silo 几乎同时尝试发起交换。
        /// </summary>
        MutualExchangeAttempt
    }
}
