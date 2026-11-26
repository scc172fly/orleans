---
title: "亚马逊 Kinesis 流"
description: "配置 Orleans的亚马逊 Kinesis数据流,包括耐用 DynamoDB 检查站。"
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="stream-with-amazon-kinesis"></a>

# 亚马逊 Kinesis 流

[`Microsoft.Orleans.Streaming.Kinesis`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Kinesis) 套件将 Orleans 持久性流连接到 [亚马孙 Kinesis 数据流](https://docs.aws.amazon.com/streams/latest/dev/introduction.html)。每个 Kinesis 碎片是一个 Orleans 队列,因此打开的碎片数量会约束物理阅读平行。 Kinesis 保留决定消费者可以重放多远。

在启动 Orleans之前创建 Kinesis 数据流。 提供者发现它的碎片, 但不会创建、 删除、 拆分或合并流 。

<a id="configure-the-silo"></a>

## 配置 silo

安装软件包并注册一个名符其实的供应商:

:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="kinesis_hosting_using":::
:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="configure_kinesis_silo":::

`PubSubStore` 坚持明确 Orleans 串行订阅。检查站表格有不同的目的:它记录每个碎片的最后交付的 Kinesis 序列号。

配置每个 Orleans 客户端, 以同一服务提供方名称、 流名和地区通过服务提供方发布 :

:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="configure_kinesis_client":::

当没有配置明确的证书时, 提供者使用 [.NET 证书分辨率链的AWS SDK](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)。 在生产中, 更喜欢工作量证书, 如 IAM 角色。 如果使用自定义 Kinesis 兼容的端点, 请设置 <xref:Orleans.Streaming.Kinesis.KinesisStreamOptions.Service> 。

<a id="choose-checkpoint-storage"></a>

## 选择检查站储存

Kinesis 碎片代号是暂时的。 Orleans 因此,将最后一个交付序列编号存放在 Kinesis 外, 并在重新启动或队列重新配置后使用它恢复运行 。

<a id="dynamodb-table-checkpoints"></a>

### DynamoDB 表式检查站

呼叫 <xref:Orleans.Hosting.SiloKinesisStreamConfigurator.UseDynamoDBCheckpointer*> 直接将检查站存放在 DynamoDB。

- 每个 Orleans 服务、提供者和Kinesis 碎片使用一个版本的物品。
- 使用一致的读数和有条件写数,以防止先前的队列所有人背信一个新检查站。
- 默认创建点名表格。当基础设施提供方拥有该表时,将 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.CreateIfNotExists> 设为 `false` 。
- 限制使用 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.PersistInterval>写。短间隔会减少失败后的重播,但会增加 DynamoDB 的写流量。

当表格使用提供的能力时,设定 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.UseProvisionedThroughput>、 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.ReadCapacityUnits>和 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.WriteCapacityUnits> 。

<a id="grain-checkpoints"></a>

### Grain 检查站

如果未选择检查站, 供应商使用 Orleans grain- 后盾检查站。 检查点 grains 默认使用 `PubSubStore` , 因此供应商的生产必须耐久:

:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="kinesis_streams_using":::
:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="configure_grain_checkpoints":::

这两种执行都保留单调 Kinesis 序列编号,并且可以在不干净的关闭后重放少量已经交付的记录。 消费者必须容忍重复交付。

<a id="operations-and-permissions"></a>

## 操作和许可

允许应用程序只使用 Kinesis 数据平面和配置所需的 DynamoDB 表权限。 Kinesis 供应商列出碎片、获取碎片代用器、读取记录并写下记录。 由供应商管理的检查站表格还需要描述和创建表格以及阅读和有条件地写入检查站项目的权限。

监视 Kinesis 代数年龄, 读取抽动、 提供输送量和与 [Orleans 流流量度](streaming-operations.md#observe-health)一起保留。 <xref:Orleans.Streaming.Kinesis.KinesisStreamOptions.GetRecordsInterval> 默认为每个碎片的 Kinesis 允许的最快速间隔 。

不支持重新刻录。 如果在提供商运行期间, 硬结构会发生变化, 接收器停止而不是冒不正确的队列所有权风险 。 在拆分或合并碎片后重新启动 Orleans 流提供者 。

使用 DynamoDB 组合、 grain 状态、提醒和Kinesis检查站的完整配置见 [AWS Kinesis 和 DynamoDB 样本](https://github.com/dotnet/orleans/tree/main/samples/AWS/KinesisDynamoDB)。
