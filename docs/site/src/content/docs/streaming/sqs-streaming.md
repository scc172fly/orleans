---
title: "亚马逊流"
description: "配置 Orleans的亚马逊 SQS 流,包括 FIFO 订单、 自定义数据调整器和操作设置。"
ms.date: 09/04/2026
ms.topic: how-to
---

<a id="stream-with-amazon-sqs"></a>

# 亚马逊流

[`Microsoft.Orleans.Streaming.SQS`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS) 软件包将 Orleans 持久性流连接到 [亚马逊简单队列服务](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html)。 Orleans 地图流跨越一套可配置的 SQS 队列,在首次使用已映射分区时创建队列,通过持续流拉动剂接收批次,并在成功发送后删除信件。

SQS 流提供 [交货后至少一次交付](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/standard-queues-at-least-once-delivery.html)。 当信件 [可见度超时](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html) 在 Orleans 确认之前过期时, 消息再次可见, 所以消费者必须处理重复。 服务商在信件到达后指定接收器- 本地序列标记, 不支持倒回到较早的标记 。

<a id="configure-a-standard-queue-provider"></a>

## 配置标准队列提供者

在 silo上安装软件包并注册一个命名的提供者:

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_silo":::

`PubSubStore` 仍然有明确的 Orleans 流订阅。 SQS独立保留事件信息,而配置的 grain 存储则保留订阅元数据。

配置每个通过供应商发布同一提供者名称、连接和分区数的客户端 Orleans :

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_client":::

下 `Service` 接受 AWS 区域,例如 `us-east-1` 或 SQS 兼容的终点,例如 `http://localhost:4566`与区域相比,提供方使用 [.NET 证书分辨率链的AWS SDK](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html) 当连接字符串没有明确的证书时, 连接字符串中含有明确证书。 最好使用工作量证书, 如 IAM 角色等。 受保护的连接字符串可以提供 `AccessKey`, `SecretKey`,和 `SessionToken` 当部署需要明确的临时全权证书时。

<a id="preserve-per-stream-order-with-fifo-queues"></a>

## 使用 FIFO 队列维护每流顺序

设置 <xref:Orleans.Configuration.SqsOptions.FifoQueue> 使用 SQS FIFO 队列:

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_fifo":::

提供者将 `.fifo` 后缀附加到队列名称,并为 FIFO 管道配置每个新队列。它从完整的 <xref:Orleans.Runtime.StreamId>中获取 SQS 信息组,因此 SQS 在独立处理不同流时保存一个 Orleans 流中的发布顺序。每份出版物都收到独特的重复编号,同时保留相同有效载荷的重复事件。

收到的 FIFO 批量显示 `SQSFIFOSequenceToken`。 该牌子带有SQS序列号,用于同一流订购,并带有接收器-本地序列号,用于 Orleans 缓存进度。 FIFO 交付仍然可以在确认失败或可见度超时后重复发送信息,因此处理仍然是无能的。

使用相同 `FifoQueue` 和分区值 silos 以及出版客户。改变任何一种值都会产生一个新的队列表层,需要明确的裁员,从而排干以前的队列。

<a id="use-an-application-wire-format"></a>

## 使用应用程序有线格式

默认 <xref:Orleans.Streaming.SQS.Streams.SQSDataAdapter> 将 Orleans 相继分批成 Orleans- 特定信息体。当外部应用程序需要生成或消耗信件时,或者当应用程序要求版本有效载荷合同时,执行 <xref:Orleans.Streaming.SQS.Streams.ISQSDataAdapter> 。

在每个 silo 和出版客户中注册相同的适配器:

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_data_adapter":::

适配器转换为 `Amazon.SQS.Model.Message` 和 <xref:Orleans.Streams.IBatchContainer>。发送时,服务商使用适配器的电文体和应用定义的信件属性,然后提供队列 URL和 FIFO 传输字段。接收时,适配器获得 SQS 消息和一个接收器-本地序列编号。

列出 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageAttributes>中的解码器所需的每个应用程序定义属性。列表要求在 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageSystemAttributes>中 SQS 系统属性; FIFO 模式自动要求 SQS 序列编号。保持流身份、批量内事件排序、预版和请求文本处理稳定地在生产商和消费者之间运行。见 [自定义持久性流数据格式](data-adapters.md) ,用于提供电线合同和推出指南。

<a id="tune-queue-and-cache-behavior"></a>

## 音调队列和缓存行为

| 设置设置设置设置 | 运行时行为 |
|---|---|
| <xref:Orleans.Hosting.SiloSqsStreamConfigurator.ConfigurePartitioning*> | 设置物理的 SQS 队列计数。 更多的队列会增加拉动剂平行性, 并创建更多要操作的队列。 将值保持对 silos 和客户端的相同 。 |
| <xref:Orleans.Hosting.SiloSqsStreamConfigurator.ConfigureCache*> | 设置每个拉动剂使用的闭合在模内缓存大小。 以事件率、 消费滞后和可用 silo 内存大小大小 。 |
| <xref:Orleans.Configuration.SqsOptions.ReceiveWaitTimeSeconds> | 启用 SQS 长的民意测验以接收请求, 在 Orleans 创建队列时设置队列默认值。 长的民调会减少空的收件和申请费用 。 |
| <xref:Orleans.Configuration.SqsOptions.VisibilityTimeoutSeconds> | 当 Orleans 创建队列时,设定队列可见度超时。选择一个比预期的交付和确认时间长的值;过期后使信件有资格重新发送。 |
| <xref:Orleans.Configuration.SqsOptions.ReceiveMessageAttributes> | 请求自定义数据适配器所消耗的应用程序定义属性。 |
| <xref:Orleans.Configuration.SqsOptions.ReceiveMessageSystemAttributes> | 请要求提供商或应用适配器所消耗的SQS系统属性。 |

当队列不存在时, 将应用队列设置。 管理现有队列通过 SQS 管理对保留、 可见度、 加密、 访问政策和死字母重新驱动政策的更改 。

序列 Orleans 批量必须适合 [SQS 信息配额](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/quotas-messages.html)。在信息接近服务限额之前,要批量齐数,并在计算中包括自定义信封和信归属间接费用。

<a id="permissions-and-operations"></a>

## 许可和操作

运行时在发送接收器时查找并创建已映像的队列、发送和接收信件、分批删除发送的信件、删除发来的信件,并重置待发邮件可视性,以便替换接收器能够迅速恢复交付。 `sqs:GetQueueUrl`, `sqs:CreateQueue`, `sqs:SendMessage`, `sqs:ReceiveMessage`, `sqs:DeleteMessage`,和 `sqs:ChangeMessageVisibility` 用于其队队列名称范围的权限。 [Amazon SQS API 权限引用](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-api-permissions-reference.html),批量的APP使用相应的 `sqs:DeleteMessage` 和 `sqs:ChangeMessageVisibility` 权限。 通过 <xref:OrleansAWSUtils.Streams.SQSStreamProviderUtils.DeleteAllUsedQueues*> 也要求 `sqs:DeleteQueue`.

监控 SQS 队列深度、 最古老消息的年代、 接收数、 空接收、 删除失败、 和与 [Orleans 流流量度](streaming-operations.md#observe-health)一起出现无字母移动。 最老消息年龄的上升表明拉动代理或消费者的速度不及时。 重复收到显示处理失败、 确认失败或者可见性超时短于端到端端传送延迟 。
