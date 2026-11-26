---
title: "Orleans NuGet 包"
description: "选择主机、提供者、序列化、可观察性和测试的 Orleans 软件包。"
ms.date: 08/21/2026
ms.topic: reference
---

<a id="orleans-nuget-packages"></a>

# Orleans NuGet 包

官方软件包使用 `Microsoft.Orleans` 前缀。 在 [NuGet.org](https://www.nuget.org/profiles/Orleans)上查找已释放的软件包。保持 Orleans 软件包版本在应用程序中一致。

<a id="start-with-a-metapackage"></a>

## 以一个元包装开始

| 套包 | 用来 |
| --- | --- |
| [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server) | 申请的东道国 Orleans silo包括运行时间、SDK、客户API和记忆耐久性提供者。 |
| [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client) | 与一个集群连接而不主机 silo的独立进程。它包括SDK。 |
| [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk) | Grain 合同或执行图书馆需要 Orleans 分析器、源产生、序列化和核心API。 |

大多数应用应首先从其中的一个软件包开始,然后只添加其使用的供应商和特写软件包。

关于安装指南,见 [`dotnet package add`](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-add) 和 [NuGet 套件安装工作流程](https://learn.microsoft.com/nuget/consume-packages/overview-and-workflow)。

<a id="hosting-and-observability"></a>

## 东道主和可遵守

| 套包 | 目的目的宗旨 |
| --- | --- |
| [Microsoft.Orleans.Hosting.Kubernetes](https://www.nuget.org/packages/Microsoft.Orleans.Hosting.Kubernetes) | Kubernetes 托管融合。 |
| [Microsoft.Orleans.Dashboard](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard) | Built-in Orleans 机身服务器和UI |
| [Microsoft.Orleans.Dashboard.Abstractions](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard.Abstractions) | 不属于UI的部件的布板合同 |

`Microsoft.Orleans.Runtime`、 `Microsoft.Orleans.Core`和抽象包件是元包装的较低依赖度。只有在建立一个有较窄依赖要求的图书馆时才直接参考它们。

<a id="clustering"></a>

## 集群组合

每个多silo 生产组群都需要一个共同的成员提供方。

| 套包 | 后端 |
| --- | --- |
| [Microsoft.Orleans.Clustering.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AdoNet) | ADO.NET- 兼容的关联关系数据库 |
| [Microsoft.Orleans.Clustering.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AzureStorage) | Azure 表格储存 |
| [Microsoft.Orleans.Clustering.Cassandra](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cassandra) | 阿帕奇卡桑德拉 |
| [Microsoft.Orleans.Clustering.Consul](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Consul) | HashiCorp 领事 |
| [Microsoft.Orleans.Clustering.Cosmos](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cosmos) | Azure 宇宙 DB |
| [Microsoft.Orleans.Clustering.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.DynamoDB) | 亚马逊 DynamoDB |
| [Microsoft.Orleans.Clustering.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Firestore) | Google 云烟火炉 |
| [Microsoft.Orleans.Clustering.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Redis) | Redis |
| [Microsoft.Orleans.Clustering.ZooKeeper](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.ZooKeeper) | 阿帕切 ZooKeeper |

<a id="grain-persistence"></a>

## Grain 持久性

| 套包 | 后端 |
| --- | --- |
| [Microsoft.Orleans.Persistence.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) | ADO.NET- 兼容的关联关系数据库 |
| [Microsoft.Orleans.Persistence.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AzureStorage) | Azure 表格和布束存储 |
| [Microsoft.Orleans.Persistence.Cosmos](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Cosmos) | Azure 宇宙 DB |
| [Microsoft.Orleans.Persistence.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB) | 亚马逊 DynamoDB |
| [Microsoft.Orleans.Persistence.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) | Google 云烟火炉 |
| [Microsoft.Orleans.Persistence.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Redis) | Redis |
| [Microsoft.Orleans.Persistence.Memory](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Memory) | 开发和测试所需的进程内存 |

本地、单项silo 开发和测试的文件存储包含在 [Microsoft.Orleans.Runtime](https://www.nuget.org/packages/Microsoft.Orleans.Runtime)中。

内存持久性将记录分布于集束存储 grains ,但不可持久或复制。只有在丢失记录时使用该记录,托管程序是可以接受的。

<a id="reminders"></a>

## 提醒提醒

| 套包 | 目的目的宗旨 |
| --- | --- |
| [Microsoft.Orleans.Reminders](https://www.nuget.org/packages/Microsoft.Orleans.Reminders) | 核心提醒支持。 |
| [Microsoft.Orleans.Reminders.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.AdoNet) | ADO.NET 存储提醒信息。 |
| [Microsoft.Orleans.Reminders.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.AzureStorage) | Azure 表格存储提醒通知。 |
| [Microsoft.Orleans.Reminders.Cosmos](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Cosmos) | Azure 宇宙DB提醒 |
| [Microsoft.Orleans.Reminders.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.DynamoDB) | 亚马逊 DynamoDB 提醒 |
| [Microsoft.Orleans.Reminders.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Firestore) | 谷歌云火炉提醒。 |
| [Microsoft.Orleans.Reminders.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Redis) | Redis 提醒函。 |

使用提醒提醒来重复重复耐用回调。 Grain 计时器是启动范围,使用核心运行时间而不是供应商软件包。

<a id="durable-state-jobs-messaging-and-tasks"></a>

## 国家、就业、信息传递和任务

| 套包 | 目的目的宗旨 |
| --- | --- |
| `Microsoft.Orleans.Durable` | 释放前的alpha日记持久状态、耐久工作、收件箱和发件箱信息以及耐用 grain RPC运行时间;见 [日刊概览](../grains/journaling/index.md)。等待第一个 NuGet 出版物。 |
| `Microsoft.Orleans.Durable.Abstractions` | 释放前的阿尔法编程模型抽象和耐用非同步操作分析器。等待第一期 NuGet 出版物。 |
| `Microsoft.Orleans.Durable.Http` | 释放前甲型 ASP.NET 核心整合的核心任务,作为人道主义过渡工作队的一等能干的业务,为持久的任务服务。 NuGet 出版。 |
| `Microsoft.Orleans.Durable.AzureStorage` | 发布前用于日记和持久工作的 Alpha Azure Blob和 Azure 表提供者。等待第一批 NuGet 出版物。 |
| `Microsoft.Orleans.Durable.Redis` | 释放前alpha Redis 日记存储提供者。等待第一批 NuGet 出版物。 |
| `Microsoft.Orleans.Durable.S3` | 释放前Alpha Amadamaz S3Express One Zone 日记存储供应商。等待第一批 NuGet 出版物。 |

将持久工作用于安排的一次性工作。 通信包含可恢复、 最不晚时的可回收的《日刊》 grain 独立地在 silo 建筑者。 这些包目标 .NET 11 (`net11.0`).

<a id="streams-and-broadcast-channels"></a>

## 流和广播频道

| 套包 | 目的目的宗旨 |
| --- | --- |
| [Microsoft.Orleans.Streaming](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) | 核心 Orleans 流APIs和运行时间。 |
| [Microsoft.Orleans.Streaming.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AdoNet) | ADO.NET- 支持的流。 |
| [Microsoft.Orleans.Streaming.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AzureStorage) | Azure 队列存储流。 |
| [Microsoft.Orleans.Streaming.EventHubs](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.EventHubs) | Azure 事件枢纽流。 |
| [Microsoft.Orleans.Streaming.Kinesis](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Kinesis) | 亚马逊金尼斯数据流 |
| [Microsoft.Orleans.Streaming.NATS](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.NATS) | NATS JetStream 流。 |
| [Microsoft.Orleans.Streaming.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Redis) | Redis 流集成。 |
| [Microsoft.Orleans.Streaming.SQS](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS) | 亚马逊SQS流。 |
| [Microsoft.Orleans.BroadcastChannel](https://www.nuget.org/packages/Microsoft.Orleans.BroadcastChannel) | 轻量级广播频道。 |

<a id="grain-directories"></a>

## Grain 目录

自自自 grain 目录目录提供者存储 grain 选定选定的登记信息 grain 类型。

| 套包 | 后端 |
| --- | --- |
| [Microsoft.Orleans.GrainDirectory.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AdoNet) | ADO.NET- 兼容的关联关系数据库 |
| [Microsoft.Orleans.GrainDirectory.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AzureStorage) | Azure 表格储存 |
| [Microsoft.Orleans.GrainDirectory.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Firestore) | Google 云烟火炉 |
| [Microsoft.Orleans.GrainDirectory.Redis](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Redis) | Redis |

这些软件包不能取代集束成员提供商。

<a id="state-models-and-transactions"></a>

## 国家模型和交易

| 套包 | 目的目的宗旨 |
| --- | --- |
| [Microsoft.Orleans.EventSourcing](https://www.nuget.org/packages/Microsoft.Orleans.EventSourcing) | 源代码为 grain 基型和日志一致性抽取。 |
| [Microsoft.Orleans.Transactions](https://www.nuget.org/packages/Microsoft.Orleans.Transactions) | 分配交易运行时间。 |
| [Microsoft.Orleans.Transactions.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.AzureStorage) | Azure 储存交易状态。 |
| [Microsoft.Orleans.Transactions.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.DynamoDB) | 亚马逊 DynamoDB 交易状态。 |

<a id="serialization"></a>

## 逐步化

Orleans 源源源源源源源源源源序列器,用于附加说明的应用类型。在使用另一种格式或生态系统时添加集成包。

| 套包 | 目的目的宗旨 |
| --- | --- |
| [Microsoft.Orleans.Serialization.SystemTextJson](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson) | System.Text.Json 整合。 |
| [Microsoft.Orleans.Serialization.MessagePack](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.MessagePack) | MessagePack 整合。 |
| [Microsoft.Orleans.Serialization.Protobuf](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.Protobuf) | 《议定书缓冲装置》的整合。 |
| [Microsoft.Orleans.Serialization.NewtonsoftJson](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.NewtonsoftJson) | Newtonsoft.Json 整合。 |
| [Microsoft.Orleans.Serialization.FSharp](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.FSharp) | F# 核心类型支助。 |

`Microsoft.Orleans.Serialization` 和 `Microsoft.Orleans.Serialization.Abstractions` 是SDK和集成使用的较低级包件。

<a id="testing"></a>

## 测试

| 套包 | 目的目的宗旨 |
| --- | --- |
| [Microsoft.Orleans.TestingHost](https://www.nuget.org/packages/Microsoft.Orleans.TestingHost) | 主机可配置的进程中测试组 。 |
| [Microsoft.Orleans.Clustering.TestKit](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.TestKit) | [成员供应商合规性测试](../implementation/provider-authoring.md#membership-provider-conformance),包括 亲生关系产生的行为史。 |
| [Microsoft.Orleans.Serialization.TestKit](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.TestKit) | 验证自定义序列器行为 。 |
| [Microsoft.Orleans.Transactions.TestKit.Base](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.TestKit.Base) | 共享交易测试包。 |
| [Microsoft.Orleans.Transactions.TestKit.xUnit](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.TestKit.xUnit) |  交易测试包的 xUnit 集成。 |

多项目应用程序中的软件包安装命令,见 [构建您的第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md)。
