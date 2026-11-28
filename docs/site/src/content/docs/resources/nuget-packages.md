---
title: Orleans NuGet 包
description: 为宿主、提供程序、序列化、可观测性和测试选择 Orleans 包。
ms.date: 08/21/2026
ms.topic: reference
---

<a id="orleans-nuget-packages"></a>

# Orleans NuGet 包

官方包使用 `Microsoft.Orleans` 前缀。可在 [NuGet.org](https://www.nuget.org/profiles/Orleans) 上查找已发布的包。应用内各 Orleans 包的版本应保持一致。

<a id="start-with-a-metapackage"></a>

## 从元包开始

| 包 | 适用场景 |
| --- | --- |
| [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server) | 托管 Orleans Silo 的应用。包含运行时、SDK、客户端 API 及内存持久化提供程序。 |
| [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client) | 不托管 Silo、仅连接到集群的独立进程。包含 SDK。 |
| [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk) | 需要 Orleans 分析器、源代码生成、序列化及核心 API 的 Grain 契约或实现库。 |

大多数应用应从这些包之一开始，然后仅添加实际使用的提供程序包及功能包。

有关安装指导，请参阅 [`dotnet package add`](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-add) 和 [NuGet 包安装工作流程](https://learn.microsoft.com/nuget/consume-packages/overview-and-workflow)。

<a id="hosting-and-observability"></a>

## 托管和可观测性

| 包 | 用途 |
| --- | --- |
| [Microsoft.Orleans.Hosting.Kubernetes](https://www.nuget.org/packages/Microsoft.Orleans.Hosting.Kubernetes) | Kubernetes 托管集成。 |
| [Microsoft.Orleans.Dashboard](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard) | 内置 Orleans Dashboard 服务端和 UI。 |
| [Microsoft.Orleans.Dashboard.Abstractions](https://www.nuget.org/packages/Microsoft.Orleans.Dashboard.Abstractions) | 供不托管 UI 的组件使用的 Dashboard 契约。 |

`Microsoft.Orleans.Runtime`、`Microsoft.Orleans.Core` 以及抽象层包是元包的底层依赖项。仅在构建依赖范围更窄的库时直接引用它们。

<a id="clustering"></a>

## 集群成员资格

每个多 Silo 生产集群都需要共享的成员资格提供程序。

| 包 | 后端 |
| --- | --- |
| [Microsoft.Orleans.Clustering.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AdoNet) | 兼容 ADO.NET 的关系数据库 |
| [Microsoft.Orleans.Clustering.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AzureStorage) | Azure Table Storage |
| [Microsoft.Orleans.Clustering.Cassandra](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cassandra) | Apache Cassandra |
| [Microsoft.Orleans.Clustering.Consul](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Consul) | HashiCorp Consul |
| [Microsoft.Orleans.Clustering.Cosmos](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cosmos) | Azure Cosmos DB |
| [Microsoft.Orleans.Clustering.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.DynamoDB) | Amazon DynamoDB |
| [Microsoft.Orleans.Clustering.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Firestore) | Google Cloud Firestore |
| [Microsoft.Orleans.Clustering.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Redis) | Redis |
| [Microsoft.Orleans.Clustering.ZooKeeper](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.ZooKeeper) | Apache ZooKeeper |

<a id="grain-persistence"></a>

## Grain 持久化

| 包 | 后端 |
| --- | --- |
| [Microsoft.Orleans.Persistence.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) | 兼容 ADO.NET 的关系数据库 |
| [Microsoft.Orleans.Persistence.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AzureStorage) | Azure Table Storage 和 Blob Storage |
| [Microsoft.Orleans.Persistence.Cosmos](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Cosmos) | Azure Cosmos DB |
| [Microsoft.Orleans.Persistence.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB) | Amazon DynamoDB |
| [Microsoft.Orleans.Persistence.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) | Google Cloud Firestore |
| [Microsoft.Orleans.Persistence.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Redis) | Redis |
| [Microsoft.Orleans.Persistence.Memory](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Memory) | 用于开发和测试的进程内存 |

[Microsoft.Orleans.Runtime](https://www.nuget.org/packages/Microsoft.Orleans.Runtime) 包含用于本地单 Silo 开发和测试的文件存储。

内存持久化将记录分布到集群存储 Grain 中，但既不持久化也不复制这些记录。仅在能够接受记录随宿主进程丢失时使用它。

<a id="reminders"></a>

## 提醒器

| 包 | 用途 |
| --- | --- |
| [Microsoft.Orleans.Reminders](https://www.nuget.org/packages/Microsoft.Orleans.Reminders) | 核心提醒器支持。 |
| [Microsoft.Orleans.Reminders.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.AdoNet) | ADO.NET 提醒器存储。 |
| [Microsoft.Orleans.Reminders.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.AzureStorage) | Azure Table Storage 提醒器。 |
| [Microsoft.Orleans.Reminders.Cosmos](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Cosmos) | Azure Cosmos DB 提醒器。 |
| [Microsoft.Orleans.Reminders.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.DynamoDB) | Amazon DynamoDB 提醒器。 |
| [Microsoft.Orleans.Reminders.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Firestore) | Google Cloud Firestore 提醒器。 |
| [Microsoft.Orleans.Reminders.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Redis) | Redis 提醒器。 |

使用提醒器实现周期性持久化回调。Grain 定时器的作用域为激活实例，使用核心运行时而非提供程序包。

<a id="durable-state-jobs-messaging-and-tasks"></a>

## 持久化状态、作业、消息传递和任务

| 包 | 用途 |
| --- | --- |
| `Microsoft.Orleans.Durable` | 预发布 Alpha 版的日志化持久状态、持久化作业、收件箱和发件箱消息传递，以及持久化 Grain RPC 运行时；请参阅 [Journaling 总览](../grains/journaling/index.md)。尚待首次发布到 NuGet。 |
| `Microsoft.Orleans.Durable.Abstractions` | 预发布 Alpha 版的持久化异步操作编程模型抽象与分析器。尚待首次发布到 NuGet。 |
| `Microsoft.Orleans.Durable.Http` | 预发布 Alpha 版的 ASP.NET Core 集成，将持久化任务作为幂等 HTTP 操作提供服务。尚待首次发布到 NuGet。 |
| `Microsoft.Orleans.Durable.AzureStorage` | 预发布 Alpha 版的 Journaling 和持久化作业 Azure Blob 与 Azure Table 提供程序。尚待首次发布到 NuGet。 |
| `Microsoft.Orleans.Durable.Redis` | 预发布 Alpha 版的 Redis 日志存储提供程序。尚待首次发布到 NuGet。 |
| `Microsoft.Orleans.Durable.S3` | 预发布 Alpha 版的 Amazon S3 Express One Zone 日志存储提供程序。尚待首次发布到 NuGet。 |

使用持久化作业安排一次性工作。消息传递将 Durable Jobs 与 Journaling 组合起来，提供可恢复的、至少一次投递的 Grain 消息。每项功能分别在 Silo 构建器上注册。这些包面向 .NET 11（`net11.0`）。

<a id="streams-and-broadcast-channels"></a>

## 流和广播通道

| 包 | 用途 |
| --- | --- |
| [Microsoft.Orleans.Streaming](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) | Orleans Streams 核心 API 和运行时。 |
| [Microsoft.Orleans.Streaming.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AdoNet) | 以 ADO.NET 为后端的流。 |
| [Microsoft.Orleans.Streaming.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AzureStorage) | Azure Queue Storage 流。 |
| [Microsoft.Orleans.Streaming.EventHubs](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.EventHubs) | Azure Event Hubs 流。 |
| [Microsoft.Orleans.Streaming.Kinesis](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Kinesis) | Amazon Kinesis Data Streams. |
| [Microsoft.Orleans.Streaming.NATS](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.NATS) | NATS JetStream 流。 |
| [Microsoft.Orleans.Streaming.Redis](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Redis) | Redis Streams 集成。 |
| [Microsoft.Orleans.Streaming.SQS](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS) | Amazon SQS 流。 |
| [Microsoft.Orleans.BroadcastChannel](https://www.nuget.org/packages/Microsoft.Orleans.BroadcastChannel) | 轻量级广播通道。 |

<a id="grain-directories"></a>

## Grain 目录

自定义 Grain 目录提供程序存储所选 Grain 类型的注册信息。

| 包 | 后端 |
| --- | --- |
| [Microsoft.Orleans.GrainDirectory.AdoNet](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AdoNet) | 兼容 ADO.NET 的关系数据库 |
| [Microsoft.Orleans.GrainDirectory.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AzureStorage) | Azure Table Storage |
| [Microsoft.Orleans.GrainDirectory.Firestore](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Firestore) | Google Cloud Firestore |
| [Microsoft.Orleans.GrainDirectory.Redis](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Redis) | Redis |

这些包不能替代集群成员资格提供程序。

<a id="state-models-and-transactions"></a>

## 状态模型和事务

| 包 | 用途 |
| --- | --- |
| [Microsoft.Orleans.EventSourcing](https://www.nuget.org/packages/Microsoft.Orleans.EventSourcing) | 基于事件溯源的 Grain 基类型及日志一致性抽象。 |
| [Microsoft.Orleans.Transactions](https://www.nuget.org/packages/Microsoft.Orleans.Transactions) | 分布式事务运行时。 |
| [Microsoft.Orleans.Transactions.AzureStorage](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.AzureStorage) | Azure Storage 事务状态存储。 |
| [Microsoft.Orleans.Transactions.DynamoDB](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.DynamoDB) | Amazon DynamoDB 事务状态存储。 |

<a id="serialization"></a>

## 序列化

Orleans 通过源代码生成为带有相应注解的应用类型生成序列化器。使用其他格式或生态时，请添加集成包。

| 包 | 用途 |
| --- | --- |
| [Microsoft.Orleans.Serialization.SystemTextJson](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson) | System.Text.Json 集成。 |
| [Microsoft.Orleans.Serialization.MessagePack](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.MessagePack) | MessagePack 集成。 |
| [Microsoft.Orleans.Serialization.Protobuf](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.Protobuf) | Protocol Buffers 集成。 |
| [Microsoft.Orleans.Serialization.NewtonsoftJson](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.NewtonsoftJson) | Newtonsoft.Json 集成。 |
| [Microsoft.Orleans.Serialization.FSharp](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.FSharp) | F# 核心类型支持。 |

`Microsoft.Orleans.Serialization` 和 `Microsoft.Orleans.Serialization.Abstractions` 是 SDK 和各集成使用的底层包。

<a id="testing"></a>

## 测试

| 包 | 用途 |
| --- | --- |
| [Microsoft.Orleans.TestingHost](https://www.nuget.org/packages/Microsoft.Orleans.TestingHost) | 托管可配置的进程内测试集群。 |
| [Microsoft.Orleans.Clustering.TestKit](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.TestKit) | [成员资格提供程序契约符合性测试](../implementation/provider-authoring.md#membership-provider-conformance)，包括 Accordant 生成的行为历史记录。 |
| [Microsoft.Orleans.Serialization.TestKit](https://www.nuget.org/packages/Microsoft.Orleans.Serialization.TestKit) | 验证自定义序列化器行为。 |
| [Microsoft.Orleans.Transactions.TestKit.Base](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.TestKit.Base) | 共享事务测试工具包。 |
| [Microsoft.Orleans.Transactions.TestKit.xUnit](https://www.nuget.org/packages/Microsoft.Orleans.Transactions.TestKit.xUnit) | 事务测试工具包的 xUnit 集成。 |

有关多项目应用中的包安装命令，请参阅[构建第一个 Orleans 应用](../quickstarts/build-your-first-orleans-app.md)。
