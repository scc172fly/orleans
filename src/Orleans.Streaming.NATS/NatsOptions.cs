using System.Text.Json;
using Orleans.Runtime;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace Orleans.Streaming.NATS;

/// <summary>
/// 配置 NATS JetStream 流提供程序。
/// </summary>
public class NatsOptions
{
    /// <summary>
    /// 获取或设置提供程序使用的 NATS JetStream 流名称。
    /// </summary>
    public string StreamName { get; set; } = default!;

    /// <summary>
    /// 获取或设置 NATS 客户端选项。
    /// </summary>
    /// <remarks>
    /// 当此值为 <see langword="null"/> 时，提供程序创建名为
    /// <c>Orleans-{providerName}</c> 的客户端，连接到 <c>localhost:4222</c>。
    /// </remarks>
    public NatsOpts? NatsClientOptions { get; set; }

    /// <summary>
    /// 获取或设置单批获取的最大消息数。
    /// 此值必须至少为 1。
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 获取或设置 NATS JetStream 流中的分区数量。
    /// 此值必须至少为 1。
    /// 此值决定确定性的主题分区方案，必须与为此提供程序配置的
    /// Orleans 流队列数量一致。
    /// </summary>
    /// <remarks>
    /// 提供程序首次启动时创建流及其主题映射。
    /// 配置的分区数量发生变化时，提供程序会在启动期间尝试更新现有流定义；
    /// 如果 NATS 服务器拒绝更新请求，启动将失败。
    /// 有关详细信息，请参见
    /// <see href="https://docs.nats.io/nats-concepts/subject_mapping#deterministic-subject-token-partitioning">
    /// 确定性的主题令牌分区。
    /// </see>.
    /// </remarks>
    public int PartitionCount { get; set; } = 8;

    /// <summary>
    /// 获取或设置用于向 NATS JetStream 发送流消息的连接数。
    /// 此值必须至少为 1。
    /// </summary>
    public int ProducerCount { get; set; } = 8;

    /// <summary>
    /// 获取或设置提供程序使用的 JSON 序列化器选项。
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// 获取或设置 NATS JetStream 集群中的流副本数量。
    /// </summary>
    /// <remarks>
    /// 较大的值可提高节点重启期间的可用性。为满足法定人数（quorum）要求，推荐奇数，
    /// 且该值不能超过集群大小。值为 <c>3</c> 时，三节点集群
    /// 可容忍单节点故障。
    /// </remarks>
    public int NumReplicas { get; set; } = 1;

    /// <summary>
    /// 获取或设置 NATS JetStream 流使用的存储后端。
    /// </summary>
    /// <remarks>
    /// <see cref="StreamConfigStorage.File"/> 在 NATS 服务器重启后仍提供持久性。
    /// <see cref="StreamConfigStorage.Memory"/> 将消息保存在内存中。所选存储类型
    /// 必须在 NATS 服务器上启用。
    /// </remarks>
    public StreamConfigStorage StorageType { get; set; } = StreamConfigStorage.File;
}

/// <summary>
/// 验证具名 NATS 流提供程序的 <see cref="NatsOptions"/>。
/// </summary>
/// <param name="options">要验证的选项。</param>
/// <param name="name">流提供程序名称。</param>
public class NatsStreamOptionsValidator(NatsOptions options, string? name = null) : IConfigurationValidator
{
    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(options.StreamName))
        {
            throw new OrleansConfigurationException(
                $"The {nameof(NatsOptions.StreamName)} is required for the NATS stream provider '{name}'.");
        }

        if (options.NumReplicas < 1)
        {
            throw new OrleansConfigurationException(
                $"The {nameof(NatsOptions.NumReplicas)} must be at least 1 for the NATS stream provider '{name}'.");
        }

        if (options.BatchSize < 1)
        {
            throw new OrleansConfigurationException(
                $"The {nameof(NatsOptions.BatchSize)} must be at least 1 for the NATS stream provider '{name}'.");
        }

        if (options.PartitionCount < 1)
        {
            throw new OrleansConfigurationException(
                $"The {nameof(NatsOptions.PartitionCount)} must be at least 1 for the NATS stream provider '{name}'.");
        }

        if (options.ProducerCount < 1)
        {
            throw new OrleansConfigurationException(
                $"The {nameof(NatsOptions.ProducerCount)} must be at least 1 for the NATS stream provider '{name}'.");
        }

        if (!Enum.IsDefined(typeof(StreamConfigStorage), options.StorageType))
        {
            throw new OrleansConfigurationException(
                $"The {nameof(NatsOptions.StorageType)} value '{options.StorageType}' is not valid for the NATS stream provider '{name}'. " +
                $"Valid values are {nameof(StreamConfigStorage.File)} and {nameof(StreamConfigStorage.Memory)}.");
        }
    }
}
