---
title: "自定义持久性流数据格式"
description: "为版本的队列消息合同配置 Orleans 持久性流数据适配器。"
ms.date: 08/18/2026
ms.topic: how-to
---

<a id="customize-persistent-stream-data-formats"></a>

# 自定义持久性流数据格式

持续流数据适配器翻译 Orleans 事件批次和提供者本地队列信息之间。 它拥有电线合同:流身份、事件类型和有效载荷编码、系统版本以及跨越运输边界的任何请求-文本值。 供应商的队列适配器继续拥有交通接入、分割分配、接收识别、缓存和回收等功能。

当运输保持不变,应用程序需要版本有效载荷格式时,使用数据调整器,需要用在外部生成的信息 Orleans,或者需要发布外部消费者可以解码的信息。 [自定义队列适配器](custom-queue-adapter.md) 何时 Orleans 还需要新的运输工具或不同的队列语义。

<a id="choose-the-provider-extension-point"></a>

## 选择提供者扩展点

内置提供商揭露了这些数据适应的扩展点:

| 提供商 | 数据适应合同 | 登记登记 | 运行时间结果 |
|---|---|---|---|
| Azure 队列存储 | <xref:Orleans.Streams.IQueueDataAdapter`2> 与 `string` 队列信件和 <xref:Orleans.Streams.IBatchContainer> 批次 | <xref:Orleans.Hosting.AzureQueueStreamConfiguratorExtensions.ConfigureQueueDataAdapter*> | 替换编码和解码,同时保留 Azure 队列绘图、可见度、删除和非可逆交付 |
| Azure 活动枢纽 | <xref:Orleans.Streaming.EventHubs.IEventHubDataAdapter> | <xref:Orleans.Hosting.EventHubStreamConfiguratorExtensions.UseDataAdapter*> | 替换电线格式、流图和缓存转换行为,同时保留事件枢纽分隔读取、检查站和可倒送 |
| 亚马逊 SQS | <xref:Orleans.Streaming.SQS.Streams.ISQSDataAdapter> | <xref:Orleans.Hosting.SiloSqsStreamConfigurator.UseDataAdapter*> 和 <xref:Orleans.Hosting.ClusterClientSqsStreamConfigurator.UseDataAdapter*> | 替换信件正文、应用程序属性和批次解码,同时保留 SQS 队列映图、 FFIFO 运输字段、 删除和非可回送交付 |

<xref:Orleans.Streams.IQueueDataAdapter`1> 定义了从 Orleans 批次转换成本地队列信息。 <xref:Orleans.Streams.IQueueDataAdapter`2> 将本地电文转换为 Orleans交付的批量集装箱。具体供应商的合同可以增加其运输所需的位置和缓存操作。

<a id="define-a-versioned-contract"></a>

## 界定已改订的合同

定义运输合同时,不考虑CLR组装名称和 Orleans 序号内装。

- 一种原型版本;
- 流行命名空间和密钥;
- 稳定的事件类型识别符号;
- 一个或多个事件有效载荷;以及
- 一套明确的跨进程元数据。

将每条消息指定为一 <xref:Orleans.Runtime.StreamId>。 保持从流号到物理分区的映射稳定, 这样一条流的事件会保留提供者的订单行为。 适配器接收批次, 因此信封必须保存每批中的事件顺序 。

将请求背景作为明确的合同处理。此示例带有字符串相关标识,并在生产应用程序中留下过程-当地价值。

<a id="implement-an-azure-queue-data-adapter"></a>

## 执行 Azure 队列数据适配器

以下的适配器写了版本 JSON 和再 <xref:Orleans.Streams.IBatchContainer> 何时 Azure 队列存储返回消息。 Azure 收件器用品队列 `sequenceId` 批量使用它为每个事件创建接收器- 本地序列符号。

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="azure_queue_data_adapter":::

批量容器只披露与要求的流类型相符的事件,从队列消息序列中获取每件事件标记,并输入有线合同界定的元数据:

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="azure_queue_batch_container":::

在使用该软件的每个 silo 客户和 Orleans 客户中,为供应商登记相同的适配器、队列服务客户和体格队列名称。 Silos 在出版时使用该适配器读写;客户在发布时使用它。

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="azure_queue_data_adapter_registration":::

配置耐久 `PubSubStore` grain 存储,并同时进行 [Orleans 流流提供者](stream-providers.md#azure-queue-storage)中所述的登记。

<a id="implement-an-event-hubs-data-adapter"></a>

## 执行事件枢纽数据调整器

对于事件枢纽, 源自其缓存邮件代表和检查站行为符合应用程序时的 <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter> 。 覆盖 :

- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.GetStreamIdentity*> 将每个 `EventData` 实例映射成溪流;
- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.GetBatchContainer*> 为 Orleans 消费者解码缓存有效载荷;
- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.ToQueueMessage*> 编码通过 Orleans公布的事件;
- 选择物理事件枢纽分割键的<xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.GetPartitionKey*> 。

适配器还参与通过 <xref:Orleans.Streaming.EventHubs.IEventHubDataAdapter>进行缓存转换和序列定位。在制造批量标牌时,保留事件枢纽的抵消和序列号,以便检查站和倒带行为与分区日志保持一致。

在 silos 和出版客户中以同一供应商名称注册适配器和事件枢纽连接。 silo 登记还配置耐用 Azure 表格检查站:

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="event_hub_data_adapter_registration":::

[自定义数据调整器样本](https://github.com/dotnet/orleans/tree/main/samples/Streaming/CustomDataAdapter) 演示外部制片人发来的 JSON 信息读边活动枢纽调整器。端至端互操作性工作流程见 [整合外部流的生产者和消费者](external-streams.md) 。

<a id="implement-an-amazon-sqs-data-adapter"></a>

## 安装一个亚马逊 SQS 数据适配器

执行 <xref:Orleans.Streaming.SQS.Streams.ISQSDataAdapter> 转换 `Amazon.SQS.Model.Message` 和 <xref:Orleans.Streams.IBatchContainer>。外向转换提供信息正文和应用程序定义的邮件属性。提供者指定了队列,在 FIFO 模式下,则指定了信件组和重复编号。入门转换将接收 SQS 消息和一个接收器-本地序列号。

在每个 silo 和出版客户端中以相同的供应商名称注册同一适配器。对编码器要求的每个属性配置 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageAttributes> 和 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageSystemAttributes> 。汇编登记和业务指导请见 [亚马逊流](sqs-streaming.md#use-an-application-wire-format) 。

<a id="evolve-the-wire-contract"></a>

## 推进电线合同

使用扩大和合同推出:

1. 部署接受当前版本和下一版剧本的读者。
1. 更改作者以发布下一版本 。
1. 保留旧的读取器, 直到队列或事件记录保存窗口不再包含旧版本 。
1. 在核查队列深度、最古老的讯息年代和检查站位置后删除旧版本。

保持服务商名、 流身份编码、 分区映射、 队列模式和序列解释稳定, 在只使用有效载荷的迁移中保持稳定。 对任何这些值的变化是流式或恢复式迁移,需要单独的切换计划。

转换失败表面作为流流交付失败。 对错误的有效载荷、无支持版本和缺失的路径元数据进行描述性例外,以便供应商根据其发送语义保留或重播源信息。 在运输作业过程中监测转换失败和隔离毒物信息,然后它们用尽留存或阻隔分块的进展。

测试两个转换方向, 使用每个部署的系统版本的留存信息。 包括不同批次、 请求背景、 重复交付、 错误包装、 未知版本、 滚动升级以及旧事件枢纽检查站的重播 。
