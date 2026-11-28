---
title: 为什么选择 Orleans
description: 了解 Orleans 虚拟 Actor 模型的优点及权衡。
ms.date: 08/08/2026
ms.topic: overview
---

<a id="why-orleans"></a>

# 为什么选择 Orleans

Orleans 将虚拟 Actor 编程模型与用于放置、消息传递、生命周期及故障检测的运行时服务相结合，帮助 .NET 开发者构建有状态的分布式应用。

其主要优势并非让分布式特性变得不可见。网络调用仍可能失败，存储必须配置，工作负载也仍需容量规划。Orleans 减少了应用为了寻址和协调大量独立实体所需自行构建的基础设施。

<a id="stable-identities-instead-of-locations"></a>

## 稳定标识取代位置

应用代码根据接口和键寻址 Grain。Orleans 将逻辑标识映射到激活实例并向其路由调用。调用方无须维护服务器注册表，也不必在放置位置改变时重新创建引用。

由于标识独立于激活实例，Orleans 可以按需激活 Grain，并从内存中移除空闲激活实例。应用可以表示的逻辑实体数量远超内存能够同时容纳的数量。

[Orleans 虚拟 Actor 研究](https://www.microsoft.com/research/project/orleans-virtual-actors/) 将这一机制比作虚拟内存：应用代码可以寻址很大的 Grain 逻辑空间，而运行时决定哪些激活实例当前驻留在内存中，以及驻留的位置。

<a id="isolated-turn-based-execution"></a>

## 隔离的轮次式执行

每个 Grain 激活实例封装其行为与状态。默认情况下，每次只处理一个请求。相较于共享内存并发，这使实体级不变量更容易推理，并消除了典型 Grain 代码中对锁的需求。

边界是显式的：Grain 调用为异步调用，并可能跨进程或机器。这促使开发者在 API 设计中考虑延迟、序列化、取消及故障。

<a id="natural-partitioning"></a>

## 自然分区

将领域实体映射为 Grain，可以按标识对状态和工作进行分区。Orleans 可以在整个集群中放置这些激活实例，并在调用方无须知道位置的情况下路由调用。

当负载分散到许多 Grain 键时，这种设计效果最佳。热点 Grain 仍然是热点 Grain：Orleans 不会自动复制普通有状态 Grain 以提高其吞吐量。应用应选择适合工作负载的 Grain 边界、无状态工作者、分层聚合或其他模式。

<a id="managed-activation-lifecycle"></a>

## 受管理的激活生命周期

Orleans 在 Grain 收到调用时激活它们，并可停用空闲激活实例以回收内存。Silo 发生故障后，成员资格收敛后到达的调用可在健康 Silo 上重新激活 Grain。

激活恢复不等同于状态复制。易失的内存状态会随进程丢失。持久化恢复需要配置存储提供程序，并成功调用持久化 API。

<a id="composable-runtime-services"></a>

## 可组合的运行时服务

Orleans 为以下功能提供一致的托管与编程抽象：

- 集群成员资格及客户端发现。
- 激活实例放置及重新平衡。
- Grain 持久化及事务。
- 定时器、提醒器及持久化作业。
- 流及广播通道。
- 序列化、版本管理、安全及可观测性。

提供程序包将这些抽象与 Azure Storage、Azure Cosmos DB、关系数据库、DynamoDB、Redis、Event Hubs、SQS、NATS、Consul、Cassandra、ZooKeeper 及 Kubernetes 等基础设施集成。

<a id="familiar-net-development"></a>

## 熟悉的 .NET 开发方式

Grain 契约是具有异步方法的 .NET 接口，Grain 实现是类，宿主使用 .NET 通用宿主及依赖注入。Orleans 也与 ASP.NET Core、日志、配置、OpenTelemetry、Aspire 及标准测试工具集成。

由此得到的分布式编程模型仍具有鲜明的 .NET 特征，同时将位置、生命周期及路由交由运行时负责，而不是让应用编写基础设施代码。

<a id="tradeoffs"></a>

## 权衡

Orleans 并非最适合所有工作负载。当应用主要需要以下能力时，应考虑其他设计：

- 在组件之间共享可变内存。
- 少量大型、高度并行的计算作业。
- 每次请求都进行全局协调。
- 实体标识价值很低的批量数据处理。

对于合适的领域，Orleans 提供高效的默认架构。它不会消除分布式系统中的权衡，但为应用解决这些问题提供经过检验的基础。
