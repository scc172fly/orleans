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
/// 单个持久化任务的持久化状态。
/// </summary>
/// <remarks>
/// <para>
/// 每个持久化成员都是基本类型、枚举、时间戳、字符串、<see cref="GrainId"/> 或订阅者集合（每个订阅者包含一个 Grain
/// 标识及一个任务标识符），因此任何日志格式都可以存储该状态。请求及结果是
/// 可由 Orleans 序列化的多态值，
/// 作为不透明的 Orleans 序列化有效载荷保存在 <see cref="RequestPayload"/> 和 <see cref="ResultPayload"/> 中。有效载荷
/// 不随状态一起序列化：每次更新都会重写状态，所以所属存储将每个有效载荷
/// 单独持久化一次，并将其附接到所公开的状态。
/// </para>
/// <para>
/// 所属存储在公开每个状态之前，先将其绑定到 <see cref="Serializer"/>。<see cref="Request"/> 和
/// <see cref="Result"/> 在首次访问时解码各自的有效载荷并缓存解码后的值，因此恢复不会解码
/// 所有有效载荷，后续更新会复用已存储的有效载荷。无法再解码的有效载荷在每次访问时都会抛出异常，
/// 并在存储中保持不变，以便能够解码它的代码（例如后续部署的代码）读取它。
/// </para>
/// <para>
/// JSON 日志省略未设置的 Grain 标识，因此恢复后的值为 <see langword="default"/>。
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
    /// 获取或设置 Orleans 序列化后的 <see cref="IDurableTaskRequest"/>；当任务没有已存储的请求时，为 <see langword="null"/>。
    /// </summary>
    /// <remarks>所属存储将有效载荷与状态分开持久化。</remarks>
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
    /// 获取或设置 Orleans 序列化后的终态 <see cref="DurableTaskResponse"/>；当任务没有已存储的结果时，为 <see langword="null"/>。
    /// </summary>
    /// <remarks>所属存储将有效载荷与状态分开持久化。</remarks>
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
    /// <remarks>首次访问时从 <see cref="ResultPayload"/> 解码该值。</remarks>
    /// <exception cref="InvalidOperationException">该状态存储了结果，但尚未绑定到其存储。</exception>
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
    /// <remarks>首次访问时从 <see cref="RequestPayload"/> 解码该值。</remarks>
    /// <exception cref="InvalidOperationException">该状态存储了请求，但尚未绑定到其存储。</exception>
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
    /// 将此状态绑定到用于编码和解码其有效载荷的序列化器。
    /// </summary>
    internal DurableTaskState Bind(Serializer serializer)
    {
        Debug.Assert(_serializer is null || ReferenceEquals(_serializer, serializer), "A durable task state is owned by one storage.");
        _serializer = serializer;
        return this;
    }

    /// <summary>获取所属存储单独持久化的有效载荷是否已附接。</summary>
    internal bool PayloadsAttached => _payloadsAttached;

    /// <summary>附接所属存储与恢复状态分开持久化的有效载荷。</summary>
    internal void AttachPayloads(byte[]? request, byte[]? result)
    {
        Debug.Assert(!_payloadsAttached, "A state's payloads are attached once.");
        RequestPayload = request;
        ResultPayload = result;
        _payloadsAttached = true;
    }

    /// <summary>
    /// 共享此实例所复制自的状态的有效载荷。有效载荷存储后不可变。
    /// </summary>
    internal void CopyPayloadsFrom(DurableTaskState source)
    {
        RequestPayload = source._requestPayload;
        ResultPayload = source._resultPayload;
    }

    /// <summary>
    /// 将 <paramref name="request"/> 存储为 Orleans 序列化有效载荷，并将其缓存为解码后的值。
    /// </summary>
    internal void SetRequest(IDurableTaskRequest? request)
    {
        _requestPayload = request is null ? null : GetSerializer().SerializeToArray(request);
        _request = request;
        _payloadsAttached = true;
    }

    /// <summary>
    /// 将 <paramref name="result"/> 存储为 Orleans 序列化有效载荷，并将其缓存为解码后的值。
    /// </summary>
    internal void SetResult(DurableTaskResponse? result)
    {
        _resultPayload = result is null ? null : GetSerializer().SerializeToArray(result);
        _result = result;
    }

    private Serializer GetSerializer() =>
        _serializer ?? throw new InvalidOperationException("The durable task state is not bound to a durable task storage.");
}
