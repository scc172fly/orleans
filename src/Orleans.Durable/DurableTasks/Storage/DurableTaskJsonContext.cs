using System.Text.Json.Serialization;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Durable.Messaging;

namespace Orleans.Durable.Tasks.Storage;

/// <summary>
/// 用于适配器在 JSON 日志中存储的任务状态及待处理调用的源生成 JSON 元数据，
/// 以及用于 Grain 存储在自身日志化状态中的任务地址的源生成 JSON 元数据。
/// </summary>
[JsonSerializable(typeof(DurableTaskState))]
[JsonSerializable(typeof(PendingInvocation))]
[JsonSerializable(typeof(DurableTaskAddress))]
[JsonSerializable(typeof(MessageId))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(byte[]))]
internal sealed partial class DurableTaskJsonContext : JsonSerializerContext;
