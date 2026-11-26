using System.Text.Json.Serialization;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;

namespace Orleans.Durable.Tasks.Storage;

/// <summary>
/// Source-generated JSON 调适器存储的任务状态和待处理任务状态的元数据 JSON 杂志,
/// 任务对象 grains 以自己的日记状态存储。
/// </summary>
[JsonSerializable(typeof(DurableTaskState))]
[JsonSerializable(typeof(PendingInvocation))]
[JsonSerializable(typeof(DurableTaskAddress))]
[JsonSerializable(typeof(MessageId))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(byte[]))]
internal sealed partial class DurableTaskJsonContext : JsonSerializerContext;
