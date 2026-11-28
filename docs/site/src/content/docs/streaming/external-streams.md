---
title: 集成外部流生产者和消费者
description: 通过定义提供程序特定的传输格式适配器，将非 Orleans 应用程序连接到 Orleans 流。
ms.date: 08/18/2026
ms.topic: how-to
---

<a id="integrate-external-stream-producers-and-consumers"></a>

# 集成外部流生产者和消费者

Orleans 持久化流提供程序通过提供程序特定的传输格式将传输消息映射到逻辑流。当所有参与方就以下内容达成一致时，外部应用程序即可通过该格式实现互操作：

- 传输消息如何映射到 Orleans <xref:Orleans.Runtime.StreamId>。
- 如何编码事件类型、有效载荷及批次。
- 如何表示分区、排序和序列位置。
- 哪些元数据（例如请求上下文）可以跨越此边界。

提供程序的[数据适配器](data-adapters.md) 负责此边界。Azure Event Hubs 提供外部生产者和消费者所需的完整入站及出站映射扩展点。

<a id="use-an-event-hubs-data-adapter"></a>

## 使用 Event Hubs 数据适配器

内置 Azure Event Hubs 提供程序支持自定义 <xref:Orleans.Streaming.EventHubs.IEventHubDataAdapter>。当 <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter> 的缓存表示及检查点行为符合需求时，可从该类型派生，再重写外部契约定义的传输格式和流映射行为。

使用 <xref:Orleans.Hosting.EventHubStreamConfiguratorExtensions.UseDataAdapter*> 注册适配器。为使用该提供程序的每个 Silo 和 Orleans 客户端配置同一个适配器。Silo 需要它来读取 Event Hubs 消息及通过 Orleans 流发布消息；Orleans 客户端在发布消息时需要它。

[自定义数据适配器示例](https://github.com/dotnet/orleans/tree/main/samples/Streaming/CustomDataAdapter) 展示了向 Event Hubs 写入 JSON 的外部生产者，以及消费相应流的 Orleans Grain。

<a id="accept-events-from-an-external-producer"></a>

## 接收外部生产者的事件

1. 定义传输契约。包含足以标识流命名空间、流键、事件类型和架构版本的元数据。
1. 将每个 `EventData` 实例映射到 <xref:Orleans.Runtime.StreamId>，并在 `GetStreamIdentity` 中执行该映射。默认 Event Hubs 适配器使用 Event Hubs 分区键作为流键，并使用 `StreamNamespace` 应用程序属性作为流命名空间。
1. 在适配器返回的 <xref:Orleans.Streams.IBatchContainer> 中解码有效载荷。`GetEvents<T>` 必须仅返回与所请求 Orleans 流类型兼容的事件。
1. 注册适配器，并让 Grain 或 Orleans 客户端订阅相应的流 ID。
1. 在生产环境启用该生产者之前，测试格式错误的有效载荷、未知架构版本、重复投递及从较早检查点进行重放。

适配器确定逻辑流标识；Event Hubs 决定物理分区、偏移量、入队时间及保留窗口。保持流到分区的映射稳定，以便排序和检查点恢复行为可预测。

<a id="publish-events-for-an-external-consumer"></a>

## 为外部消费者发布事件

1. 重写 `ToQueueMessage<T>`，以外部消费者约定的格式编码 Orleans 事件。
1. 当外部契约使用不同的分区映射时，重写 `GetPartitionKey`。
1. 保留流标识及架构版本元数据，以便消费者无需 Orleans 即可路由和解码事件。
1. 将外部应用程序配置为普通 Event Hubs 消费者，最好为该应用程序配置专用消费者组。

外部消费者直接参与 Event Hubs。其消费者组控制投递、检查点、重试及重放行为，而 Orleans 发布/订阅存储和订阅通知适用于 Orleans 流订阅。

如果适配器有意仅支持单向转换，应在不支持的转换方法中显式报告失败。例如，仅用于接收数据的适配器可抛出 <xref:System.NotSupportedException>，具体在 `ToQueueMessage<T>` 中执行，而不是输出非预期格式的消息。

<a id="operational-requirements"></a>

## 运维要求

- 为 Orleans 提供程序使用专用 Event Hubs 消费者组，并为每个独立外部消费者使用不同的组。
- 仅授予生产者、消费者及检查点存储所需的权限。将凭据和连接字符串保存在受保护的配置中，而不是事件属性中。
- 对有效载荷契约进行版本管理，添加新格式时先部署读取端，再部署写入端。
- 预期会发生重复投递，并确保消费者具有幂等性。Event Hubs 保留期限和 Orleans 检查点持久化机制限定了重放和恢复范围。
- 监控适配器反序列化失败、Event Hubs 滞后、检查点存留时长、缓存压力及有毒事件。
- 对适配器进行负载测试。有效载荷膨胀、反序列化开销及分区键分布不均可能先于 Grain 执行成为吞吐量瓶颈。

有关事件进入 Orleans 后的投递和恢复行为，参见[流投递语义](delivery-semantics.md) 和[运维 Orleans 流处理应用程序](streaming-operations.md)。
