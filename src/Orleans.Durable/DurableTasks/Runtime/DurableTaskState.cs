using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Orleans.Serialization;

namespace Orleans.Durable.Tasks.Runtime;

/// <summary>
/// 一项持久任务持续存在。
/// </summary>
/// <remarks>
/// <para>
/// 每个成员都是原始的、点数、时间戳、字符串、 <see cref="GrainId"/>或一组订户,每个 grain
/// 身份和任务标识符, 所以每个日记格式都能够存储状态。 请求和结果
/// 多态 Orleans- 可序列值,以
/// Orleans- <see cref="RequestPayload"/> 和 <see cref="ResultPayload"/>中的定序有效载荷。
/// 与状态没有序列化: 状态每更新一次都重新写入, 所以拥有的存储器会持续每个有效载荷
/// 单独,一次,并把它附加到它所暴露的国家。
/// </para>
/// <para>
/// 拥有所有权的储存库将每个国家约束于其 <see cref="Serializer"/> 之前,公开它。 <see cref="Request"/> 和
/// <see cref="Result"/> 在第一次进入时解码其有效载荷,并隐藏已解码值,因此回收不会编码
/// 不再编码的有效载荷将每一次进入和每次进入都投下,
/// 保存不变,所以代码解码 它,例如后期部署的,读它。
/// </para>
/// <para>
/// JSON 日记省去未设置的 grain 身份,因此恢复为 <see langword="default"/>。
/// </para>
/// </remarks>
[GenerateSerializer, SuppressReferenceTracking]
[Alias("Orleans.Durable.Tasks.Runtime.DurableTaskState")]
internal class DurableTaskState : IDurableTaskState
{
    [NonSerialized]
    private Serializer? _serializer;
    [NonSerialized]
    private byte[]? _requestPayload;
    [NonSerialized]
    private byte[]? _resultPayload;
    [NonSerialized]
    private IDurableTaskRequest? _request;
    [NonSerialized]
    private DurableTaskResponse? _result;
    [NonSerialized]
    private bool _payloadsAttached;

    /// <summary>
    /// 获取或设置 Orleans- 序列化的 <see cref="IDurableTaskRequest"/>,或者当任务没有存储请求时,获取或设置 <see langword="null"/> 。
    /// </summary>
    /// <remarks>拥有的储存库将有效载荷与状态分开。</remarks>
    [JsonIgnore]
    public byte[]? RequestPayload
    {
        get => _requestPayload;
        set
        {
            _requestPayload = value;
            _request = null;
        }
    }

    /// <summary>
    /// 获取或设置 Orleans- 序列终端 <see cref="DurableTaskResponse"/>,或者当任务没有存储结果时,获取或设置 <see langword="null"/> 。
    /// </summary>
    /// <remarks>拥有的储存库将有效载荷与状态分开。</remarks>
    [JsonIgnore]
    public byte[]? ResultPayload
    {
        get => _resultPayload;
        set
        {
            _resultPayload = value;
            _result = null;
        }
    }

    /// <inheritdoc cref="IDurableTaskState.Result"/>
    /// <remarks>数值解码 <see cref="ResultPayload"/> 第一次访问。</remarks>
    /// <exception cref="InvalidOperationException">国家储存是结果,但不受储存的约束。</exception>
    [JsonIgnore]
    public DurableTaskResponse? Result => _result ??= _resultPayload is { } payload
        ? GetSerializer().Deserialize<DurableTaskResponse>(payload)
        : null;

    /// <inheritdoc cref="IDurableTaskState.Kind"/>
    [Id(23)]
    public DurableTaskRecordKind Kind { get; set; }

    /// <inheritdoc cref="IDurableTaskState.CompletionDestinations"/>
    [Id(6)]
    public HashSet<DurableTaskSubscriber> CompletionDestinations { get; set; } = [];

    /// <inheritdoc cref="IDurableTaskState.Request"/>
    /// <remarks>数值解码 <see cref="RequestPayload"/> 第一次访问。</remarks>
    /// <exception cref="InvalidOperationException">国家储存一项请求,但不受其储存的约束。</exception>
    [JsonIgnore]
    public IDurableTaskRequest? Request => _request ??= _requestPayload is { } payload
        ? GetSerializer().Deserialize<IDurableTaskRequest>(payload)
        : null;

    /// <inheritdoc cref="IDurableTaskState.HasRequest"/>
    [JsonIgnore]
    public bool HasRequest => _requestPayload is not null;

    /// <inheritdoc cref="IDurableTaskState.CompletedAt"/>
    [Id(3)]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <inheritdoc cref="IDurableTaskState.CreatedAt"/>
    [Id(4)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc cref="IDurableTaskState.CancellationRequestedAt"/>
    [Id(5)]
    public DateTimeOffset? CancellationRequestedAt { get; set; }

    /// <inheritdoc cref="IDurableTaskState.CancellationReason"/>
    [Id(18)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CancellationReason { get; set; }

    /// <inheritdoc cref="IDurableTaskState.Shielded"/>
    [Id(19)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Shielded { get; set; }

    /// <inheritdoc cref="IDurableTaskState.Accepted"/>
    [Id(21)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Accepted { get; set; }

    [Id(7)]
    public DateTimeOffset? DueTime { get; set; }

    [Id(8)]
    public long ResumeGeneration { get; set; }

    /// <inheritdoc cref="IDurableTaskState.RequestFingerprint"/>
    [Id(9)]
    public string? RequestFingerprint { get; set; }

    /// <inheritdoc cref="IDurableTaskState.Method"/>
    [Id(24)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Method { get; set; }

    [Id(11)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public GrainId RemoteTarget { get; set; }

    /// <inheritdoc cref="IDurableTaskState.RemoteRequestFingerprint"/>
    [Id(12)]
    public string? RemoteRequestFingerprint { get; set; }

    /// <inheritdoc cref="IDurableTaskState.CallerId"/>
    [Id(13)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public GrainId CallerId { get; set; }

    /// <inheritdoc cref="IDurableTaskState.RetainedSince"/>
    [Id(17)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RetainedSince { get; set; }

    /// <inheritdoc cref="IDurableTaskState.Incarnation"/>
    [Id(22)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Guid Incarnation { get; set; }

    /// <inheritdoc cref="IDurableTaskState.CompletionSequence"/>
    [Id(20)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long CompletionSequence { get; set; }

    IReadOnlySet<DurableTaskSubscriber> IDurableTaskState.CompletionDestinations => CompletionDestinations;

    /// <summary>
    /// 将这个状态绑在序列器上 用于编码和解码其有效载荷的序列器
    /// </summary>
    internal DurableTaskState Bind(Serializer serializer)
    {
        Debug.Assert(_serializer is null || ReferenceEquals(_serializer, serializer), "A durable task state is owned by one storage.");
        _serializer = serializer;
        return this;
    }

    /// <summary>了解拥有存储器的剩余有效载荷是否被单独连接。</summary>
    internal bool PayloadsAttached => _payloadsAttached;

    /// <summary>随附拥有的储存库与回收状态分开存在的有效载荷。</summary>
    internal void AttachPayloads(byte[]? request, byte[]? result)
    {
        Debug.Assert(!_payloadsAttached, "A state's payloads are attached once.");
        RequestPayload = request;
        ResultPayload = result;
        _payloadsAttached = true;
    }

    /// <summary>
    /// 共享此实例复制的状态的有效载荷。 这些有效载荷一旦存储后就不可移动 。
    /// </summary>
    internal void CopyPayloadsFrom(DurableTaskState source)
    {
        RequestPayload = source._requestPayload;
        ResultPayload = source._resultPayload;
    }

    /// <summary>
    /// 仓库 <paramref name="request"/> 以 A 时 Orleans- 序列有效载荷并隐藏作为解码值。
    /// </summary>
    internal void SetRequest(IDurableTaskRequest? request)
    {
        _requestPayload = request is null ? null : GetSerializer().SerializeToArray(request);
        _request = request;
        _payloadsAttached = true;
    }

    /// <summary>
    /// 仓库 <paramref name="result"/> 以 A 时 Orleans- 序列有效载荷并隐藏作为解码值。
    /// </summary>
    internal void SetResult(DurableTaskResponse? result)
    {
        _resultPayload = result is null ? null : GetSerializer().SerializeToArray(result);
        _result = result;
    }

    private Serializer GetSerializer() =>
        _serializer ?? throw new InvalidOperationException("The durable task state is not bound to a durable task storage.");
}
