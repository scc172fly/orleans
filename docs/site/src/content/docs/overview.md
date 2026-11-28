---
title: Orleans 总览
description: 了解 Orleans 如何简化分布式 .NET 应用的构建。
ms.date: 08/02/2026
ms.topic: overview
---

# Microsoft Orleans

Orleans 是使用 .NET 构建分布式应用的跨平台框架。它将熟悉的 C# 概念扩展到整个集群，使应用代码能够按标识寻址有状态实体，而不必跟踪当前由哪台服务器托管它们。

Orleans 库面向 `net10.0` 和 `net11.0`。

> [!NOTE]
> 若要升级现有应用，请参阅 [Orleans 迁移指南](migration-guide.md)。特定版本的升级历史集中保存在该指南中，而不在普通文档中重复。

<a id="the-virtual-actor-model"></a>

## 虚拟 Actor 模型

Orleans 实现虚拟 Actor 模型。Orleans 中的虚拟 Actor 称为 *Grain*。每个 Grain 都具有：

- 稳定的、由应用定义的标识。
- 由 .NET 类实现的行为。
- 隔离的、可变的内存状态，以及可选的、由已配置提供程序存储的持久化状态。

Grain 引用是逻辑地址。调用方不直接创建 Grain 对象，也不在服务器上查找它们，而是根据接口及键请求引用，然后调用该引用上的异步方法。Orleans 将每次调用路由至现有激活实例，或按需激活该 Grain。

这种虚拟生命周期将 Grain 标识与内存中的激活实例分离。空闲激活实例可以从内存中移除，后续调用可以再次激活 Grain。如果已配置持久化状态且 Grain 已将其成功写入，新激活实例就可以恢复该状态。

有关此模型的设计及历史，请参阅 [Orleans 虚拟 Actor 研究项目](https://www.microsoft.com/research/project/orleans-virtual-actors/)。

## Grain

Grain 是标识、隔离、放置及执行的基本单位。Grain 类实现一个或多个 Grain 接口，通常使用以下标记接口之一确定标识形式：

- <xref:Orleans.IGrainWithGuidKey>
- <xref:Orleans.IGrainWithIntegerKey>
- <xref:Orleans.IGrainWithStringKey>
- <xref:Orleans.IGrainWithGuidCompoundKey>
- <xref:Orleans.IGrainWithIntegerCompoundKey>

默认情况下，Orleans 每次只处理发往某个 Grain 激活实例的一个请求。这种基于执行轮次的模型减少了 Grain 代码中对锁的需求。应用需要时可以采用可重入性及其他交错执行选项，但必须像处理其他并发代码一样谨慎。

:::image type="content" source="media/grain-formulation.svg" lightbox="media/grain-formulation.svg" alt-text="Grain 由稳定的标识、行为和状态组成。":::

Orleans 应用通常将用户、设备、帐户、游戏会话、订单或其他能够独立寻址的实体建模为 Grain。合理的 Grain 边界来自应用领域及工作负载：既要避免由大量细粒度 Grain 组成、通信过于频繁的网络，也要避免单个 Grain 成为吞吐量瓶颈。

<a id="silos-and-clusters"></a>

## Silo 与集群

*Silo* 是托管 Orleans 运行时及 Grain 激活实例的进程。一个或多个 Silo 组成集群。集群成员协调成员资格、路由消息及放置激活实例。

:::image type="content" source="media/cluster-silo-grain-relationship.svg" lightbox="media/cluster-silo-grain-relationship.svg" alt-text="一个集群包含一个或多个 Silo，每个 Silo 托管 Grain 激活实例。":::

Silo 发生故障时，正在进行的调用可能失败，调用方必须处理这种结果。集群检测到故障后，后续调用可以在健康 Silo 上激活受影响的 Grain。持久化恢复取决于应用的存储配置，以及状态是否已成功写入。

外部进程可以通过 Orleans 客户端库进行连接。ASP.NET Core 应用也可以在同一进程内托管 Silo 并调用 Grain，这正是初学者快速入门所采用的方式。

<a id="runtime-capabilities"></a>

## 运行时能力

Orleans 提供满足常见分布式应用需求的 API 和提供程序：

- **集群成员资格与放置**：发现 Silo 并选择激活实例的运行位置。
- **持久化**：使用 Azure Storage、Azure Cosmos DB、ADO.NET、DynamoDB、Redis 或自定义提供程序存储显式写入的 Grain 状态。
- **定时器、提醒器及持久化作业**：运行激活实例作用域内的回调、持久化周期回调或已调度的一次性工作。
- **流与广播通道**：在生产者与消费者之间投递事件。
- **事务**：使用分布式 ACID 事务协调跨 Grain 的受支持持久化状态。
- **版本管理**：在分阶段升级期间运行异构版本集群。
- **可观测性**：集成 .NET 日志、指标、分布式跟踪、健康检查及 Orleans Dashboard。
- **序列化**：生成具有版本容错能力的序列化器，并集成 System.Text.Json、MessagePack、Protocol Buffers 等格式。

这些能力可以组合使用。应用选择适合其环境的提供程序及保证；Orleans 不会自动复制应用状态，也不会自动使外部存储具备持久性。

<a id="distributed-acid-transactions"></a>

## 分布式 ACID 事务

Orleans 事务使用具备可串行化隔离级别的分布式 [ACID](https://learn.microsoft.com/windows/win32/cossdk/acid-properties) 事务，协调多个 Grain 中受支持的持久化状态。事务状态使用专用 API 和存储配置；普通 Grain 持久化不会自动具备事务能力。

有关更多信息，参阅[事务](grains/transactions.md)。

<a id="when-to-use-orleans"></a>

## 何时使用 Orleans

当应用拥有大量可独立寻址的实体、能够从实体级隔离中获益，并需要跨进程或机器分配工作时，Orleans 非常适合。常见示例包括 AI Agent 会话、反欺诈与风险管理、物联网及数字孪生、监控与资源编排、在线游戏、协作、商业及金融服务。

Orleans 可以在开发期间运行于单个进程中，并在不更改 Grain 接口的情况下扩展为集群。生产部署仍需对集群成员资格、存储、重试、可观测性、容量及部署作出明确选择。

有关带有具体示例及反例的决策指南，请参阅 [Orleans 场景及用例](scenarios.md)。

<a id="next-step"></a>

## 下一步

> [!div class="nextstepaction"]
> [Orleans Hello World](tutorials-and-samples/hello-world.md)
