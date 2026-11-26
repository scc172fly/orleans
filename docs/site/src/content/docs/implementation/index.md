---
title: "Orleans 运行时结构"
description: "程序、变量和扩展点在 Orleans 运行时间范围内的高级指南。"
ms.date: 08/02/2026
ms.topic: overview
---

<a id="orleans-runtime-architecture"></a>

# Orleans 运行时结构

实施轨迹解释了 Orleans 如何实现运行时贡献者、提供方作者和操作者的虚拟行为者模式,这些行为者因失败、一致性、时间安排和可推广性而产生理由。

本音轨中的页面使用 Orleans 源和测试作为规格。内部型号名称允许读者通过存储库跟踪操作。公共API和明确指定的扩展点带有兼容性合同。

<a id="a-runtime-map"></a>

## 运行时地图

应用程序呼叫通过生成代理代码进入,成为 `Message`,并被 grain 名录和安置服务。 `MessageCenter` 在当地或通过 `Connection` 由 `ConnectionManager`目标 `Catalog` 解析激活,当 `ActivationData` 认可请求, `WorkItemGroup` 执行同步转弯。 响应会通过同一发短信和回回回管道返回 。

运行时间故意分层:

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

使用此运行时映射和以下主题列表选择所需的深度。 [运行时间结构](runtime-architecture.md) 页遵循正常呼叫路径;信息、序列化、提醒、交易和版本skew页面解释失败、持久性和滚动升级改变担保的界限。

<a id="runtime-core"></a>

## 运行时核心

- [运行时间结构](runtime-architecture.md) 遵循通过客户、信息、职位安排、目录、启动和时间安排等组成部分发出的电话。
- [激活生命周期和移徙](activation-lifecycle.md) 解释创建、激活、收集、停止使用和州转让。
- [分组成员](cluster-management.md) 描述故障检测器、会籍表、定购意见和死亡投票协议。
- [Grain 目录](grain-directory.md) 解释默认视图同步分布目录和 `LegacyGrainDirectory` DHT。
- [排排和转手执行](scheduler.md) 解释 `WorkItemGroup`, 续编、 交接和单行距执行。
- [通信和传送语义](messaging-delivery-guarantees.md) 追踪请求,并解释为什么超时的结果不明。
- [运输和内部联网](messaging-networking.md) 解释连接设置、框架、后压和关闭行为。
- [安置和启动平衡](load-balancing.md) 涵盖默认的资源优化政策和选择加入的移动协议。

<a id="runtime-services-and-extensibility"></a>

## 运行时间服务和可推广性

- [生命周期实施](orleans-lifecycle.md) 描述有订单的启动和关闭。
- [逐步化和代码生成](serialization.md) 包括生成的编码、代号、清单、线条识别和定制部件。
- [提醒提醒](reminders.md) 解释环的所有权、耐久提醒行、刷新和滴答交付。
- [交易交易](transactions.md) 解释交易代理商、管理人员、参与者排队和追回决定。
- [滚动版本折叠](rolling-version-skew.md) 连接接口版本的显示器、兼容性主任、选择器和混合版操作中的线兼容性。
- [持久性流](streams-implementation/index.md) 解释拉动剂、队列所有权、缓存、光标、酒吧子和回收。
- [提供方作者](provider-authoring.md) 是指命名的提供者、配置约束性、生命周期参与和验证。
- [TestingHost 结构架构](testing.md) 解释了生产服务在工艺过程中的集束利用及其替代。

<a id="defaults-that-shape-the-architecture"></a>

## 构造架构的默认值

| 关注问题 | 默认默认 |
| --- | --- |
| 安置职位安排 | <xref:Orleans.Runtime.ResourceOptimizedPlacement> |
| Grain 目录 | `DistributedGrainDirectory`,使用版版范围和协调成员意见 |
| 遗产目录 | 选择 <xref:Orleans.Hosting.CoreHostingExtensions.UseLegacyGrainDirectory*?displayProperty=nameWithType> |
| 分布目录分区 | <xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo?displayProperty=nameWithType> 默认值为 1 |
| 成员探测超时 | 5秒5秒 |
| 死刑票过期 | 2分钟2分钟 |
| <xref:Orleans.Configuration.MessagingOptions.ResponseTimeout?displayProperty=nameWithType> | 30秒或30分钟,同时配有调试器 |
| 响应超时后自动调用重试 | 无无无无 |

配置值影响故障检测和资源使用。 此轨道解释它们在协议中的作用, 而 [配置指南](../host/configuration-guide/index.md) 和 [部署指导](../deployment/index.md) 则自有操作建议 。

对于应用的精神模型,从 [Orleans 概览](../overview.md)开始。对于应用这些成分的任务导向配方,使用 [如何指导索引](../how-to/index.md);对于公共类型合同,使用 [C# API 参考](https://dotnet.github.io/orleans/docs/api/csharp/)。
