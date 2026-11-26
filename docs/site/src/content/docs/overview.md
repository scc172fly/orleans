---
title: Orleans 概述
description: 了解 Orleans 如何简化 .NET 分布式应用的开发。
ms.date: 08/02/2026
ms.topic: overview
---

# Microsoft Orleans

Orleans 是一个用于构建 .NET 分布式应用的跨平台框架。它将熟悉的 C# 概念扩展到整个集群，让应用代码通过标识访问有状态实体，无需追踪当前由哪台服务器托管实体。

Orleans 库面向 `net10.0` 和 `net11.0`。

> [!NOTE]
> 如需升级现有应用，请参阅 [Orleans 迁移指南](migration-guide.md)。各版本的升级记录均收录在该指南中。

<a id="the-virtual-actor-model"></a>

## 虚拟 Actor 模型

Orleans 实现了虚拟 Actor 模型。Orleans 中的虚拟 Actor 称为 *Grain*。每个 Grain 都具有：

- 由应用定义的稳定标识。
- 由 .NET 类实现的行为。
- 相互隔离、可变的内存状态。应用还可以配置提供程序来存储持久状态。

Grain 引用是逻辑地址。调用方通过接口和键获取引用，再调用该引用上的异步方法。Orleans 会将调用路由到已有的激活实例，或按需激活 Grain。

这种虚拟生命周期将 Grain 的标识与其内存中的激活实例分离。空闲的激活实例可从内存中移除，后续调用可再次激活该 Grain。如果应用配置了持久状态且 Grain 已成功写入，新实例便可恢复该状态。

有关模型的设计与历史，请参阅 [Orleans 虚拟 Actor 研究项目](https://www.microsoft.com/research/project/orleans-virtual-actors/)。

## Grains

Grain 是标识、隔离、放置和执行的基本单元。Grain 类实现一个或多个 Grain 接口，通常通过以下标记接口之一指定键的类型：

- <xref:Orleans.IGrainWithGuidKey>
- <xref:Orleans.IGrainWithIntegerKey>
- <xref:Orleans.IGrainWithStringKey>
- <xref:Orleans.IGrainWithGuidCompoundKey>
- <xref:Orleans.IGrainWithIntegerCompoundKey>

默认情况下，Orleans 会逐个处理发给同一个 Grain 激活实例的请求。这种以轮次为单位的执行模型减少了 Grain 代码加锁的需求。应用可以启用可重入或其他交错执行选项，使用时需要像处理其他并发代码一样谨慎。

:::image type="content" source="media/grain-formulation.svg" lightbox="media/grain-formulation.svg" alt-text="Grain 由稳定的标识、行为和状态组成。":::

Orleans 应用通常用 Grain 为用户、设备、账户、游戏会话、订单及其他可独立寻址的实体建模。Grain 的边界取决于应用领域和实际负载。应避免设计大量需要频繁通信的微小 Grain，也应避免让单个 Grain 成为吞吐量瓶颈。

<a id="silos-and-clusters"></a>

## Silo 与集群

*Silo* 是托管 Orleans 运行时和 Grain 激活实例的进程。一个或多个 Silo 组成集群。集群成员共同维护成员关系、路由消息并决定激活实例的放置位置。

:::image type="content" source="media/cluster-silo-grain-relationship.svg" lightbox="media/cluster-silo-grain-relationship.svg" alt-text="集群包含一个或多个 Silo，每个 Silo 都可以托管 Grain 激活实例。":::

Silo 发生故障时，正在进行的调用可能失败，调用方必须处理这种结果。集群检测到故障后，后续调用可在健康的 Silo 上激活受影响的 Grain。能否恢复持久状态取决于应用的存储配置，以及状态是否已成功写入。

外部进程可通过 Orleans 客户端库连接集群。ASP.NET Core 应用也可以在同一进程中托管 Silo 并调用 Grain，快速入门采用的就是这种方式。

<a id="runtime-capabilities"></a>

## 运行时功能

Orleans 提供满足常见分布式应用需求的 API 和提供程序：

- **集群与放置**：发现 Silo，并选择激活实例的运行位置。
- **持久化**：使用 Azure Storage、Azure Cosmos DB、ADO.NET、DynamoDB、Redis 或自定义提供程序存储显式写入的 Grain 状态。
- **计时器、提醒和持久作业**：运行激活范围内的回调、周期性持久回调或计划内的一次性工作。
- **流与广播通道**：在生产者和消费者之间投递事件。
- **事务**：使用分布式 ACID 事务协调多个 Grain 中受支持的持久状态。
- **版本控制**：在分阶段升级期间运行异构集群。
- **可观测性**：与 .NET 日志、指标、分布式跟踪、运行状况检查及 Orleans 仪表板集成。
- **序列化**：生成能够容忍版本变化的序列化器，并与 System.Text.Json、MessagePack 和 Protocol Buffers 等格式集成。

这些功能可以组合使用。应用应根据环境选择提供程序和所需保证。应用状态复制和外部存储持久性需要由应用的存储方案提供。

<a id="distributed-acid-transactions"></a>

## 分布式 ACID 事务

Orleans 通过具有可串行化隔离级别的分布式 [ACID](https://learn.microsoft.com/windows/win32/cossdk/acid-properties) 事务，协调多个 Grain 中受支持的持久状态。事务状态需要专用 API 和存储配置，普通 Grain 持久化仅提供其原有的持久化语义。

详情请参阅[事务](grains/transactions.md)。

<a id="when-to-use-orleans"></a>

## 何时使用 Orleans

当应用具有大量可独立寻址的实体、需要隔离每个实体，且要跨进程或机器分配工作时，Orleans 很适合这类场景。常见示例包括 AI 智能体会话、欺诈防护和风险管理、物联网与数字孪生、监控与资源编排、在线游戏、协作、商业和金融服务。

Orleans 可在开发期间以单进程运行，并在无需更改 Grain 接口的情况下扩展为集群。生产部署需要根据实际环境选择集群、存储、重试、可观测性、容量及部署方案。

有关包含具体示例和反例的指南，请参阅 [Orleans 应用场景](scenarios.md)。

<a id="next-step"></a>

## 后续步骤

> [!div class="nextstepaction"]
> [Orleans Hello World](tutorials-and-samples/hello-world.md)

