---
title: "整合外部流的生产者和消费者"
description: "通过定义一个具体供应商的有线格式适配器,将非Orleans 应用程序连接到 Orleans 流。"
ms.date: 08/18/2026
ms.topic: how-to
---

<a id="integrate-external-stream-producers-and-consumers"></a>

# 整合外部流的生产者和消费者

A 音 A Orleans 持续流供应商通过特定供应商的电线格式将其运输信息绘制成逻辑流。

- 如何将传送信息映射到 Orleans <xref:Orleans.Runtime.StreamId>。
- 事件类型、有效载荷和批量如何编码。
- 分区、顺序和顺序位置的表示方式。
- 请求上下文等哪些元数据跨越边界。

供应商的 [数据数据交换器](data-adapters.md) 拥有这一边界。 Azure 事件枢纽暴露了外部生产商和消费者所需的全部入境和出境地图。

<a id="use-an-event-hubs-data-adapter"></a>

## 使用事件枢纽数据适配器

内置 Azure 事件枢纽提供商支持自定义 <xref:Orleans.Streaming.EventHubs.IEventHubDataAdapter>. Derive from <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter> 当其缓存代表和检查站行为合适时,将推翻外部合同界定的电线格式和流图行为。

注册适 <xref:Orleans.Hosting.EventHubStreamConfiguratorExtensions.UseDataAdapter*>。配置相同的适应器,用于每个 silo 和 Orleans 使用提供者的客户端。 A silo 需要它读取事件枢纽信息,并通过 Orleans 流; 流; 流; 和 Orleans 客户在发布时需要它。

[自定义数据调整器样本](https://github.com/dotnet/orleans/tree/main/samples/Streaming/CustomDataAdapter) 显示一个外部活动枢纽生产商,该制片人写 JSON 和 Orleans grain ,其中消耗了由此产生的流。

<a id="accept-events-from-an-external-producer"></a>

## 接受外部制片人的活动

1. 定义运输合同。 包含足够的元数据以识别流命名空间、 流键、 事件类型和 schema 版本 。
1. 每个地图图 `EventData` 等一至 <xref:Orleans.Runtime.StreamId> 内 `GetStreamIdentity`。默认的事件枢纽调整器使用事件枢纽分区键作为流键和 `StreamNamespace` 应用程序属性作为流体命名空间。
1. 解码适配器返回的 <xref:Orleans.Streams.IBatchContainer> 中的有效载荷。 `GetEvents<T>` 必须只返回与所要求的 Orleans 流类型相容的事件。
1. 将调整器注册并订阅 grains 或 Orleans 客户端,以获取相应的流代号。
1. 试验无效有效载荷、未知的假肢版本、重复交付和从旧检查站重播,然后才能使生产商生产。

调整器决定逻辑流身份; 事件枢纽确定物理分隔、 偏移、 排队时间和保留窗口。 保持流到区绘图稳定, 以便定购和检查站恢复可以预测 。

<a id="publish-events-for-an-external-consumer"></a>

## 对外消费者出版活动

1. 覆盖 `ToQueueMessage<T>` 以外部消费者商定的格式编码 Orleans 事件。
1. 当外部合同使用不同的分区图绘制时,覆盖 `GetPartitionKey` 。
1. 保存流特性和计划转换元数据,使消费者能够选择并解码事件,而不使用 Orleans。
1. 将外部应用程序配置为普通事件枢纽消费者, 最好是与专门处理该应用程序的消费者团体配置。

外部消费者直接参与事件枢纽。其消费者团体控制交付、检查、重试和重播行为,而 Orleans 公共子集存储和订阅通知适用于 Orleans 流订阅。

如果适配器是故意单向的,则在无支持转换方法中明显失败。例如,只使用食用 <xref:System.NotSupportedException> 调自 `ToQueueMessage<T>` 而不是以非故意格式发布消息。

<a id="operational-requirements"></a>

## 所需业务费用

- 为 Orleans 供应商使用专门的事件枢纽消费者小组,为每个独立的外部消费者使用不同的小组。
- 赠款生产者、消费者和检查站仅储存他们所需要的许可。 将证书和连接字符串保留在受保护的配置中,而不是事件属性。
- 版本有效载荷合同,并在添加新格式时在作者之前部署读者。
- 事件枢纽保留和 Orleans 检查站坚持进行重放和恢复。
- 监测适应器消毒故障、事件枢纽落后、检查站年龄、缓存压力和中毒事件。
- 装入测试适配器。 有效载荷扩展、 消空成本和斜分割键可以在 grain 执行之前限制吞吐量 。

关于事件进入 Orleans后交付和回收行为,见 [流流输送语义](delivery-semantics.md) 和 [运行 Orleans 流式应用程序](streaming-operations.md)。
