---
title: Orleans 运行时架构
description: 深入了解 Orleans 运行时内部协议、不变量与扩展点的高级指南。
ms.date: 08/02/2026
ms.topic: overview
---

<a id="orleans-runtime-architecture"></a>

# Orleans 运行时架构

实现系列文档面向运行时贡献者、提供程序作者及需要分析故障、一致性、调度与扩展性的运维人员，解释 Orleans 如何实现虚拟 Actor 模型。概念及任务导向的章节提供应用编程指导。

本系列以 Orleans 源码和测试为规范。内部类型名称帮助读者沿仓库源码追踪操作。公共 API 和明确标识的扩展点具有兼容性契约。

<a id="a-runtime-map"></a>

## 运行时全景

应用调用通过生成的代理代码进入，形成 `Message`，由 Grain 目录及放置服务进行路由。`MessageCenter` 在本地投递，或通过 `Connection` 进行投递，后者由 `ConnectionManager` 管理。目标 `Catalog` 解析激活实例，由 `ActivationData` 接纳请求，并由 `WorkItemGroup` 每次执行一个同步轮次。响应通过相同的消息传递和回调管道返回。

运行时有意采用分层设计：

```mermaid
flowchart LR
    Proxy[Generated proxy and invokable] --> Factory[MessageFactory]
    Factory --> Center[MessageCenter]
    Center --> Directory[Grain directory and placement]
    Center --> Network[ConnectionManager and transport]
    Directory --> Activation[Catalog and ActivationData]
    Activation --> Admission[Request admission]
    Admission --> Scheduler[WorkItemGroup]
    Scheduler --> Grain[Grain turn]
    Grain --> Serialization[Generated codecs and wire format]
    Serialization --> Network
```

使用此运行时全景图及以下主题列表选择所需的阅读深度。[运行时架构](runtime-architecture.md) 页面追踪正常调用路径；消息传递、序列化、提醒器、事务和滚动版本差异相关页面解释故障、持久化与滚动升级会改变哪些保证。

<a id="runtime-core"></a>

## 运行时核心

- [运行时架构](runtime-architecture.md) 追踪调用经过客户端、消息传递、放置、目录、激活和调度组件的过程。
- [激活生命周期与迁移](activation-lifecycle.md) 解释创建、激活、回收、停用及状态转移。
- [集群成员资格](cluster-management.md) 描述故障检测器、成员表、有序视图及死亡投票协议。
- [Grain 目录](grain-directory.md) 解释默认视图同步分布式目录与 `LegacyGrainDirectory` DHT。
- [调度与执行轮次](scheduler.md) 解释 `WorkItemGroup`、延续操作、交错执行及单线程执行。
- [消息传递与投递语义](messaging-delivery-guarantees.md) 追踪请求，并解释为何超时意味着执行结果未知。
- [传输与网络内部机制](messaging-networking.md) 解释连接建立、消息分帧、背压及关闭行为。
- [放置与激活实例均衡](load-balancing.md) 介绍默认资源优化策略及可选择启用的迁移协议。

<a id="runtime-services-and-extensibility"></a>

## 运行时服务及扩展性

- [生命周期实现](orleans-lifecycle.md) 描述有序的启动与关闭。
- [序列化与代码生成](serialization.md) 介绍生成的编解码器、代理、清单、传输格式标识及自定义组件。
- [提醒器](reminders.md) 解释环上的所有权、持久化提醒器记录、刷新及触发事件投递。
- [事务](transactions.md) 解释事务代理、管理器、参与者队列及恢复决策。
- [滚动部署中的版本差异](rolling-version-skew.md) 将混合版本运行期间的接口版本清单、兼容性决策器、选择器及传输格式兼容性联系起来。
- [持久化流](streams-implementation/index.md) 解释拉取代理、队列所有权、缓存、游标、发布/订阅及恢复。
- [编写提供程序](provider-authoring.md) 描述命名提供程序、配置绑定、生命周期参与及验证。
- [TestingHost 架构](testing.md) 解释进程内集群测试框架及其对生产服务的替代实现。

<a id="defaults-that-shape-the-architecture"></a>

## 影响架构的默认配置

| 事项 | 默认值 |
| --- | --- |
| 放置 | <xref:Orleans.Runtime.ResourceOptimizedPlacement> |
| Grain 目录 | `DistributedGrainDirectory`，使用带版本的范围及协调一致的成员资格视图 |
| 旧版目录 | 选择 <xref:Orleans.Hosting.CoreHostingExtensions.UseLegacyGrainDirectory*?displayProperty=nameWithType> |
| 分布式目录分区 | <xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo?displayProperty=nameWithType> 默认为 1 |
| 成员资格探测超时 | 5 秒 |
| 死亡投票过期时间 | 2 分钟 |
| <xref:Orleans.Configuration.MessagingOptions.ResponseTimeout?displayProperty=nameWithType> | 30 秒，附加调试器时为 30 分钟 |
| 响应超时后自动重试调用 | 无 |

配置值影响故障检测和资源使用。本系列解释它们在协议中的作用，[宿主配置指南](../host/configuration-guide/index.md) 和 [部署指南](../deployment/index.md) 则提供运维建议。

如需理解应用编程模型，请从 [Orleans 概述](../overview.md) 开始。应用这些组件的任务导向示例参见[操作指南索引](../how-to/index.md)；公共类型契约参见 [C# API 参考](https://dotnet.github.io/orleans/docs/api/csharp/)。
