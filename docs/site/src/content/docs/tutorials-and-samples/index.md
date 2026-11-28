---
title: Orleans 教程和示例
description: 通过教程、原理说明及仓库中的示例学习 Orleans。
ms.date: 08/02/2026
ms.topic: sample
---

<a id="orleans-tutorials-and-samples"></a>

# Orleans 教程和示例

使用此页面选择适合的学习资料：

- **快速入门** 引导你完成一项具体任务。
- **教程** 逐步讲解一种能力。
- **原理说明** 介绍如何对完整应用程序进行建模。
- **示例** 是可供查看、构建和运行的应用程序。

<a id="start-here"></a>

## 从这里开始

若要学习最精简的应用程序，请从 [Orleans Hello World](hello-world.md) 开始。它在同一进程内托管 Silo 并调用 Grain。

若要了解更典型的项目结构，请参阅 [构建第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md)，其中使用独立的 Grain 契约、实现、Silo 和外部客户端项目。

<a id="tutorials"></a>

## 教程

| 教程 | 学习内容 |
| --- | --- |
| [将 Orleans 应用程序部署到 Azure 容器应用](production-application.md) | 运行、部署、观测并验证多进程 Orleans 应用程序。 |
| [对 Orleans 应用程序进行端到端测试](testing-walkthrough.md) | 从第一个集群测试逐步扩展到可复用的测试夹具及拓扑变更测试。 |
| [构建和恢复流式应用程序](streaming-walkthrough.md) | 跟踪事件在实际提供程序中的流转，并验证从检查点恢复的过程。 |
| [自定义 Grain 存储](custom-grain-storage.md) | 实现和注册 <xref:Orleans.Storage.IGrainStorage> 提供程序。 |
| [在 Azure 上部署和扩展](../quickstarts/deploy-scale-orleans-on-azure.md) | 将 Orleans 应用部署到 Azure 容器应用并配置共享提供程序。 |

<a id="explanations"></a>

## 原理说明

| 文章 | 说明内容 |
| --- | --- |
| [Adventure 冒险游戏示例](adventure.md) | 如何将房间、玩家及物品映射为 Grain 和普通值。 |
| [为什么选择 Orleans](../benefits.md) | 虚拟 Actor 模型的优势与权衡。 |
| [Orleans 架构设计原则](../resources/orleans-architecture-principles-and-approach.md) | 决定 Orleans API 及运行时行为的设计目标。 |

<a id="samples"></a>

## 示例

[`samples` 目录](https://github.com/dotnet/orleans/tree/main/samples) 包含 Orleans 官方示例。[示例目录](https://github.com/dotnet/orleans/blob/main/samples/README.md) 由仓库清单生成，并与示例项目一起进行验证。

<a id="fundamentals"></a>

### 基础知识

| 示例 | 演示内容 |
| --- | --- |
| [Hello World](https://github.com/dotnet/orleans/tree/main/samples/HelloWorld) | 单项目中的 Silo、Grain 契约、实现、引用和调用。 |
| [Adventure](https://github.com/dotnet/orleans/tree/main/samples/Adventure) | 使用 Grain 和独立客户端进行领域建模。 |
| [Chirper](https://github.com/dotnet/orleans/tree/main/samples/Chirper) | 持久化、观察者和可重入 Grain。 |
| [Simple Streaming](https://github.com/dotnet/orleans/tree/main/samples/Streaming/Simple) | 流生产者、消费者及订阅。 |

<a id="web-and-application-integration"></a>

### Web 和应用程序集成

| 示例 | 演示内容 |
| --- | --- |
| [Shopping Cart](https://github.com/dotnet/orleans/tree/main/samples/ShoppingCart) | Blazor、持久化及多项目应用程序。 |
| [Blazor Server](https://github.com/dotnet/orleans/tree/main/samples/Blazor/BlazorServer) | 由 Grain 提供后端支持的 Blazor Server 应用程序。 |
| [Blazor WebAssembly](https://github.com/dotnet/orleans/tree/main/samples/Blazor/BlazorWasm) | 托管的 WebAssembly 客户端以及以 Orleans 为后端的服务器。 |
| [GPS Tracker](https://github.com/dotnet/orleans/tree/main/samples/GPSTracker) | ASP.NET Core SignalR 及类似物联网设备的数据更新。 |
| [Presence Service](https://github.com/dotnet/orleans/tree/main/samples/Presence) | 游戏场景中的观察者及协作 Grain。 |

<a id="state-scheduling-and-messaging"></a>

### 状态、调度和消息传递

| 示例 | 演示内容 |
| --- | --- |
| [Bank Account](https://github.com/dotnet/orleans/tree/main/samples/BankAccount) | 跨多个有状态 Grain 的 ACID 事务。 |
| [Journaled Todo List](https://github.com/dotnet/orleans/tree/main/samples/JournaledTodoList) | 使用 `JournaledGrain`、日志一致性提供程序和 Aspire 的事件溯源。 |
| [Journaling with Azure Blob JSON](https://github.com/dotnet/orleans/tree/main/samples/JournalingAzureBlobJson) | 与 Azure Blob 存储配合使用的实验性 Journaling API。 |
| [Messaging](https://github.com/dotnet/orleans/tree/main/samples/Messaging) | 类型化主题、稳定命令 ID、经日志确认的回复，以及重复提交时仅执行一次库存预留。 |
| [Chat Room](https://github.com/dotnet/orleans/tree/main/samples/ChatRoom) | 使用 Orleans Streams 的终端聊天应用程序。 |
| [Stocks](https://github.com/dotnet/orleans/tree/main/samples/Stocks) | Grain 定时器、HTTP 调用及临时缓存。 |

Azure Blob JSON 示例使用 `Microsoft.Orleans.Durable` 包中的实验性 Journaling。Journaled Todo List 使用受支持的 [Orleans 事件溯源](../grains/event-sourcing/index.md) 模型。请参阅 [Journaling 示例指南](../grains/journaling/samples.md)，了解如何运行 Azure Blob 和 Redis 持久化状态示例。

[Messaging 操作示例](../grains/messaging-recipes.md#run-the-stock-reservation-sample)
介绍如何使用本地打包的 Orleans 包运行库存预留示例。

<a id="deployment-and-operations"></a>

### 部署和运维

| 示例 | 演示内容 |
| --- | --- |
| [Azure Container Apps](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps) | 集群、客户端、仪表板、扩缩容组件及 Bicep 部署。 |
| [Azure App Service](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureAppService) | 在 App Service 上运行多实例 Orleans 应用程序。 |
| [Transport Layer Security](https://github.com/dotnet/orleans/tree/main/samples/TransportLayerSecurity) | Orleans 网络通信的双向 TLS。 |
| [Voting](https://github.com/dotnet/orleans/tree/main/samples/Voting) | 面向 Kubernetes 的部署及 Orleans 仪表板。 |

仓库的示例目录还包含 F#、Visual Basic、游戏、自定义流适配器及其他针对特定功能的示例。

<a id="validate-samples-locally"></a>

## 在本地验证示例

克隆 Orleans 仓库后，从仓库根目录运行示例验证脚本：

```powershell
pwsh ./samples/Validate-Samples.ps1
```

该脚本验证示例展示清单，并构建 `samples/Samples.slnx` 中的每个项目。仅在运行连接云服务的示例时才需要云凭据，编译这些示例不需要云凭据。
