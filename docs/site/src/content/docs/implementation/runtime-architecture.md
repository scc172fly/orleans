---
title: 运行时架构
description: 跟踪 Orleans 调用经过客户端、消息传递、放置、目录、激活和调度器的过程。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="runtime-architecture"></a>

# 运行时架构

Orleans 提供位置透明的 Grain 引用，这一抽象来自 [Orleans 虚拟 Actor 研究论文](https://www.microsoft.com/en-us/research/publication/orleans-distributed-virtual-actors-for-programmability-and-scalability/)，但运行时通过多个可独立替换或能够感知故障的子系统来实现每次调用。核心不变量是 Grain 标识稳定，而激活实例的位置是临时的。

```mermaid
flowchart LR
    App[Application code] --> Proxy[Generated grain proxy]
    Proxy --> GRef[GrainReferenceRuntime]
    GRef --> Client[OutsideRuntimeClient]
    Client --> Gateway[Client gateway]
    Gateway --> MC[MessageCenter]
    MC --> Directory[Grain locator and directory]
    Directory --> Placement[Placement service]
    Placement --> Catalog[Catalog and ActivationData]
    Catalog --> Scheduler[WorkItemGroup scheduler]
    Scheduler --> Grain[Grain method]
    Grain --> MC
    MC --> Client
```

<a id="host-composition"></a>

## 宿主组成

Silo 是一个 [.NET 通用宿主（Generic Host）](https://learn.microsoft.com/dotnet/core/extensions/generic-host)，通过 [.NET 依赖注入](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview) 注册 Orleans 服务。`SiloHostedService` 启动和停止 <xref:Orleans.Runtime.Silo>，后者驱动有序的 Silo 生命周期。默认注册集合由以下组件组成：

- `MessageCenter` 负责网络连接、路由、转发、网关及消息分派。
- `MembershipTableManager`、`MembershipAgent`、`ClusterHealthMonitor` 和 `ClusterMembershipService` 负责集群成员资格。
- `DistributedGrainDirectory`、`GrainLocator` 和 `CachedGrainLocator` 负责定位激活实例。
- `PlacementService` 和键控放置决策器负责新激活实例的位置选择。
- `ActivationDirectory`、`Catalog`、`ActivationData` 和 `ActivationCollector` 负责本地激活实例的所有权管理。
- `InsideRuntimeClient` 负责调用本地目标并生成响应。
- `DeploymentLoadPublisher` 及环境统计信息用于放置和过载决策。

参见 [`DefaultSiloServices`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Hosting/DefaultSiloServices.cs) 了解组合根；参见 <xref:Orleans.Runtime.Silo> 及其[实现](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Silo/Silo.cs) 了解生命周期编排。

外部客户端没有激活目录或放置服务。`ClusterClient` 启动 `OutsideRuntimeClient`，发现网关、维护网关连接，并通过客户端的 `MessageCenter` 发送请求。Silo 也内嵌一个运行时客户端，但其 `InsideRuntimeClient` 能直接向本地激活实例和系统目标分派消息。

<a id="a-request-through-the-runtime"></a>

## 请求如何经过运行时

1. 源代码生成器为每个 Grain 接口方法生成代理方法和可调用请求类型。
1. 代理将可调用对象传递给 `GrainReferenceRuntime`。传出调用筛选器可以检查或替换该调用。
1. 运行时创建 `Message`，分配其关联标识及目标 Grain，注册响应回调，并通过 `MessageCenter` 发送消息。
1. 客户端通过网关发送。已知激活实例地址时，Silo 可以直接路由至目标 Silo。
1. 接收端的 `MessageCenter` 解析激活实例地址。缓存命中可以避免一次目录往返。陈旧地址可以被标记为失效并重新路由。
1. 如果不存在激活实例，目录和 `PlacementService` 协调在兼容的 Silo 上创建实例。
1. `Catalog` 创建 `ActivationData`；在分派已排队的应用请求之前完成激活流程。
1. `ActivationData` 将请求交给激活实例调度器。传入调用筛选器及生成的调用器执行 Grain 方法。
1. `InsideRuntimeClient` 创建响应或拒绝消息。响应按照原始关联标识返回给等待中的回调。

相关实现为 [`GrainReferenceRuntime`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Runtime/GrainReferenceRuntime.cs)、[`OutsideRuntimeClient`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Runtime/OutsideRuntimeClient.cs)、[`MessageCenter`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Messaging/MessageCenter.cs)、[`Catalog`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Catalog/Catalog.cs) 和 [`InsideRuntimeClient`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Core/InsideRuntimeClient.cs)。

<a id="system-targets"></a>

## 系统目标

系统目标是可寻址的运行时组件，它们使用 Orleans 消息传递及单线程调度，但没有虚拟激活机制。运行时在明确指定的 Silo 地址上构造它们。成员资格服务、持久化流拉取代理以及激活实例均衡协议使用系统目标，因为它们需要消息传递和调度模型，却不能是位置透明的虚拟 Actor。

与 Grain 不同，系统目标：

- 具有具体的 Silo 位置；
- 由运行时代码创建并注册；
- 不通过放置或 Grain 目录查找来激活；以及
- 参与运行时生命周期，而非应用 Grain 的生命周期。

<a id="consistency-boundaries"></a>

## 一致性边界

Orleans 不会在这些子系统之间实现一个全局事务。每个边界都有更窄的契约：

- 成员表将成员资格更新串行化为单调有序的视图。
- 目录协调 Grain 标识与激活实例地址之间的关系，并修复陈旧的注册记录。
- 激活实例调度器串行执行单个激活实例的同步工作项。
- 消息传递将请求与响应关联，但无法推断已超时的请求是否已经执行。
- 持久化和流分别定义自己的持久性保证和确认点。

扩展运行时时必须理解这些边界。自定义目录改变的是位置一致性，而非成员资格。放置决策器决定新激活实例从何处启动，而非如何调度调用。流适配器定义队列确认机制，而非 Grain 调用的恰好一次语义。

<a id="public-extension-surfaces"></a>

## 公共扩展点

优先使用受支持的扩展点，而不是替换内部运行时类型：

- <xref:Orleans.Runtime.Placement.IPlacementDirector> 及放置筛选器自定义候选目标选择。
- <xref:Orleans.GrainDirectory.IGrainDirectory> 提供命名 Grain 目录。
- 序列化编解码器、拷贝器、激活器及转换器扩展传输格式处理。
- 流队列适配器、映射器、均衡器、缓存及故障处理程序扩展持久化流。
- <xref:Orleans.Providers.IProviderBuilder`1> 集成由配置驱动的提供程序。
- 生命周期参与者控制提供程序启动与关闭的顺序。

内部名称和算法并非兼容性保证。公共 API、分析器警告以及文档所述的传输格式标识才是兼容性边界。
