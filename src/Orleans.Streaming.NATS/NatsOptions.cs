using System.Text.Json;
using Orleans.Runtime;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;

namespace Orleans.Streaming.NATS;

/// <summary>
/// 配置 NATS JetStream 流提供商。
/// </summary>
public class NatsOptions
{
    /// <summary>
    /// 获取或设置提供者使用的 NATS JetStream 流的名称。
    /// </summary>
    public string StreamName { get; set; } = default!;

    /// <summary>
    /// 获取或设置 NATS 客户端选项 。
    /// </summary>
    /// <remarks>
    /// 当此值为 <see langword="null"/>时, 服务提供方创建了一个名为客户端的客户端
    /// <c>Orleans-{providerName}</c> ,连接到 <c>localhost:4222</c>。
    /// </remarks>
    public NatsOpts? NatsClientOptions { get; set; }

    /// <summary>
    /// 获取或设置每批中要获取的最大信件数量。
    /// 值必须至少为 1。
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 获取或设置 NATS JetStream 流中的分区数。
    /// 值必须至少为 1。
    /// 此值决定确定主题分配方案, 且必须匹配
    /// Orleans 为提供者配置的 流队列 。
    /// </summary>
    /// <remarks>
    /// 提供商在首次启动时创建了流及其主题映射。
    /// 当配置的分区计数修改时, 提供方试图更新现有流定义
    /// 启动过程中。 如果NATS 服务器拒绝要求更新, 启动失败 。
    /// 详情见
    /// <see href="https://docs.nats.io/nats-concepts/subject_mapping#deterministic-subject-token-partitioning">
    /// 定点主题
    /// </see>.
    /// </remarks>
    public int PartitionCount { get; set; } = 8;

    /// <summary>
    /// 获取或设置用于向NATS发送流信息 JetStream的连接数。
    /// 值必须至少为 1。
    /// </summary>
    public int ProducerCount { get; set; } = 8;

    /// <summary>
    /// 获取或设置供应商使用的 JSON 序列器选项。
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// 获取或设置 NATS JetStream 组群中的流复制件数量。
    /// </summary>
    /// <remarks>
    /// 节点重新启动时,更高的数值会增加可用性。建议法定人数的奇数值,
    /// 和数值不能超过组群大小。 <c>3</c> 的值可以容忍单节点失败
    /// 在一个三节组别中。
    /// </remarks>
    public int NumReplicas { get; set; } = 1;

    /// <summary>
    /// 获取或设置NATS JetStream 流所使用的存储后端。
    /// </summary>
    /// <remarks>
    /// <see cref="StreamConfigStorage.File"/> 提供NATS服务器重新启动的耐久性。
    /// <see cref="StreamConfigStorage.Memory"/> 将信件存储在内存中。选中的存储类型
    /// 必须启用NATS 服务器。
    /// </remarks>
    public StreamConfigStorage StorageType { get; set; } = StreamConfigStorage.File;
}

/// <summary>
/// 验证名为NATS流提供商的 <see cref="NatsOptions"/> 。
/// </summary>
/// <param name="options">验证的选项。</param>
/// <param name="name">流体提供者名称。</param>
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
