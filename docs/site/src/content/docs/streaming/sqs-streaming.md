---
title: 使用 Amazon SQS 进行流处理
description: 为 Orleans 配置 Amazon SQS 流，包括 FIFO 排序、自定义数据适配器以及运维设置。
ms.date: 09/04/2026
ms.topic: how-to
---

<a id="stream-with-amazon-sqs"></a>

# 使用 Amazon SQS 进行流处理

[`Microsoft.Orleans.Streaming.SQS`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS) 包将 Orleans 持久化流连接到 [Amazon Simple Queue Service](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html)。Orleans 将流映射到一组可配置的 SQS 队列，在映射的分区首次使用时创建对应队列，通过持久化流拉取代理接收批次，并在成功投递后删除消息。

SQS 流提供[至少一次投递](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/standard-queues-at-least-once-delivery.html)。如果 Orleans 确认消息之前[可见性超时](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html) 到期，消息会再次可见，因此消费者必须能够处理重复消息。提供程序在消息到达时分配接收器本地的序列令牌，不支持回溯到较早的令牌位置。

<a id="configure-a-standard-queue-provider"></a>

## 配置标准队列提供程序

安装包并在 Silo 上注册具名提供程序：

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_silo":::

`PubSubStore` 持久化显式 Orleans 流订阅。SQS 独立保留事件消息，而配置的 Grain 存储保留订阅元数据。

为通过此提供程序发布消息的每个 Orleans 客户端配置相同的提供程序名称、连接及分区数量：

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_client":::

`Service` 连接值接受 AWS 区域（例如 `us-east-1`）或 SQS 兼容端点（例如 `http://localhost:4566`）。配置区域时，如果连接字符串不含显式凭据，则提供程序使用 [AWS SDK for .NET 凭据解析链](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)。优先使用 IAM 角色等工作负载凭据。如果部署需要显式临时凭据，可以通过受保护的连接字符串提供 `AccessKey`、`SecretKey` 和 `SessionToken`。

<a id="preserve-per-stream-order-with-fifo-queues"></a>

## 使用 FIFO 队列保持单流顺序

设置 <xref:Orleans.Configuration.SqsOptions.FifoQueue> 以使用 SQS FIFO 队列：

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_fifo":::

提供程序在队列名称后附加 `.fifo` 后缀，并为每个新队列配置 FIFO 吞吐量。它根据完整的 <xref:Orleans.Runtime.StreamId> 派生 SQS 消息组，因此 SQS 保持单个 Orleans 流内的发布顺序，同时独立处理不同的流。每次发布均获得唯一的去重 ID，从而保留有效载荷相同的重复事件。

接收的 FIFO 批次公开 `SQSFIFOSequenceToken`。令牌携带 SQS 序列号以实现同一流内的排序，以及接收器本地的序列号以跟踪 Orleans 缓存进度。确认失败或可见性超时后，FIFO 投递仍可能重复投递消息，因此处理仍需具备幂等性。

在 Silo 和发布消息的客户端上使用相同的 `FifoQueue` 值及分区数量。改变任一值都会创建新的队列拓扑，需要显式切换并处理完旧队列中的消息。

<a id="use-an-application-wire-format"></a>

## 使用应用程序传输格式

默认 <xref:Orleans.Streaming.SQS.Streams.SQSDataAdapter> 将 Orleans 批次序列化为 Orleans 特定的消息正文。当外部应用程序需要生产或消费消息，或者应用程序需要带版本的有效载荷契约时，请实现 <xref:Orleans.Streaming.SQS.Streams.ISQSDataAdapter>。

在每个 Silo 和发布消息的客户端上注册同一适配器：

:::code language="csharp" source="../snippets/compiled/Streaming/SqsSnippets.cs" id="configure_sqs_data_adapter":::

适配器负责在 `Amazon.SQS.Model.Message` 和 <xref:Orleans.Streams.IBatchContainer> 之间转换。发送时，提供程序使用适配器的消息正文及应用程序定义的消息属性，然后补充队列 URL 和 FIFO 传输字段。接收时，适配器得到 SQS 消息及接收器本地序列号。

在 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageAttributes> 中列出解码器所需的每个应用程序定义的属性。在 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageSystemAttributes> 中列出所需的 SQS 系统属性；FIFO 模式自动请求 SQS 序列号。确保生产者和消费者之间的流标识、批次内事件顺序、架构版本管理和请求上下文处理保持稳定。有关传输格式契约及发布策略指导，参见[自定义持久化流数据格式](data-adapters.md)。

<a id="tune-queue-and-cache-behavior"></a>

## 调优队列和缓存行为

| 设置 | 运行时行为 |
|---|---|
| <xref:Orleans.Hosting.SiloSqsStreamConfigurator.ConfigurePartitioning*> | 设置物理 SQS 队列数量。增加队列可提高拉取代理的并行度，但会产生更多需要运维的队列。在 Silo 和客户端上保持此值相同。 |
| <xref:Orleans.Hosting.SiloSqsStreamConfigurator.ConfigureCache*> | 设置各拉取代理使用的有界内存缓存大小。根据事件速率、消费者滞后以及 Silo 可用内存确定容量。 |
| <xref:Orleans.Configuration.SqsOptions.ReceiveWaitTimeSeconds> | 为接收请求启用 SQS 长轮询，并在 Orleans 创建队列时设置队列默认值。长轮询可减少空接收及请求成本。 |
| <xref:Orleans.Configuration.SqsOptions.VisibilityTimeoutSeconds> | 在 Orleans 创建队列时设置可见性超时时间。选择长于预期投递和确认延迟的值；超时到期后消息即可重新投递。 |
| <xref:Orleans.Configuration.SqsOptions.ReceiveMessageAttributes> | 请求自定义数据适配器所使用的应用程序定义属性。 |
| <xref:Orleans.Configuration.SqsOptions.ReceiveMessageSystemAttributes> | 请求提供程序或应用程序适配器所使用的 SQS 系统属性。 |

队列创建设置在队列不存在时应用。对于现有队列，通过 SQS 管理功能修改保留期限、可见性、加密、访问策略及死信重驱动策略。

序列化后的 Orleans 批次必须符合 [SQS 消息配额](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/quotas-messages.html)。在消息接近服务限制前限制批次大小，计算时也要考虑自定义消息信封和消息属性的额外开销。

<a id="permissions-and-operations"></a>

## 权限与运维

运行时查找并创建映射的队列、发送和接收消息、批量删除已投递消息，以及在接收器交接时重置待处理消息的可见性，使替换接收器能够及时恢复投递。为应用程序的队列名称范围授予对应的 `sqs:GetQueueUrl`、`sqs:CreateQueue`、`sqs:SendMessage`、`sqs:ReceiveMessage`、`sqs:DeleteMessage` 和 `sqs:ChangeMessageVisibility` 权限。正如 [Amazon SQS API 权限参考](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-api-permissions-reference.html) 所述，批量 API 分别使用相应的 `sqs:DeleteMessage` 和 `sqs:ChangeMessageVisibility` 权限。通过 <xref:OrleansAWSUtils.Streams.SQSStreamProviderUtils.DeleteAllUsedQueues*> 进行管理清理还需要 `sqs:DeleteQueue`。

将 SQS 队列深度、最早消息存留时长、接收次数、空接收次数、删除失败及死信转移情况与 [Orleans 流处理指标](streaming-operations.md#observe-health) 一并监控。最早消息存留时长增加表明拉取代理或消费者未能跟上处理进度。重复接收表明处理失败、确认失败，或者可见性超时时间短于端到端投递延迟。
