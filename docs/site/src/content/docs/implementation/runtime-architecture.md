---
title: "运行时间结构"
description: "跟随 Orleans 通过客户端的呼叫、信息、定位、目录、启动和排程器。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="runtime-architecture"></a>

# 运行时间结构

Orleans 显示位置透明 grain 参考文献,在 [Orleans 虚拟行为者研究论文](https://www.microsoft.com/en-us/research/publication/orleans-distributed-virtual-actors-for-programmability-and-scalability/),但运行时间使用几个独立可替换或有故障知觉的子系统执行每个呼叫。 grain 身份稳定,而启动地点是临时的。

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

## 东道国构成

A silo 是 a 的 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 与 Orleans 注册服务 [.NET 依赖性注射](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/overview). `SiloHostedService` 开始和停止 <xref:Orleans.Runtime.Silo>,它驱动了命令 silo 默认注册集包括:

- `MessageCenter` ,用于网络连接、路由、转发、网关和发送。
- `MembershipTableManager`、 `MembershipAgent`、 `ClusterHealthMonitor`和 `ClusterMembershipService` 的成员资格。
- `DistributedGrainDirectory`、 `GrainLocator`和 `CachedGrainLocator` ,用于激活位置。
- `PlacementService` 和钥匙安置主任,以进行新的启动选择。
- `ActivationDirectory`、 `Catalog`、 `ActivationData`和 `ActivationCollector` ,用于当地启动所有权。
- `InsideRuntimeClient` 援引当地目标和作出响应。
- `DeploymentLoadPublisher` 和环境统计,用于安置和超负荷决定。

见见 [`DefaultSiloServices`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Hosting/DefaultSiloServices.cs) 根和 <xref:Orleans.Runtime.Silo> 加 其 [执行中](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Silo/Silo.cs) 生命周期管弦。

外部客户没有激活目录或职位安排服务。 `ClusterClient` 启动一个 `OutsideRuntimeClient`,发现网关,维护网关连接,并通过客户 `MessageCenter`发送请求。 A silo 也嵌入运行时间客户端,但其 `InsideRuntimeClient` 可以直接发送到本地的启动和系统目标。

<a id="a-request-through-the-runtime"></a>

## 运行时一直请求

1. 源生成器为每一种 grain 接口方法发布一种替代方法和可援引的请求类型。
1. 代理服务器将可引用的转至 `GrainReferenceRuntime`。 发送呼叫过滤器可以检查或替换调用 。
1. 运行时创建 `Message`,指定其相关身份和目标 grain,登记回调,并通过 `MessageCenter`发送。
1. 客户端通过网关发送。 当知道激活地址时, A silo 可以直接前往目标 silo 。
1. 接收方 `MessageCenter` 解决激活地址。缓存点击可避免目录的往返行程。一个失效地址可以失效和更改路径。
1. 如果没有激活, 则目录和 `PlacementService` 协调兼容 silo的创建。
1. `Catalog` 创建一个 `ActivationData`;在发送队列申请前启动运行。
1. `ActivationData` 将请求放在激活调度器上。接收的呼叫过滤器和生成的提示器执行 grain 方法。
1. `InsideRuntimeClient` 创建响应或拒绝。 响应遵循等待回调的原始相关身份 。

相关的履行是 [`GrainReferenceRuntime`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Runtime/GrainReferenceRuntime.cs)、 [`OutsideRuntimeClient`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Runtime/OutsideRuntimeClient.cs)、 [`MessageCenter`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Messaging/MessageCenter.cs)、 [`Catalog`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Catalog/Catalog.cs)和 [`InsideRuntimeClient`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Core/InsideRuntimeClient.cs)。

<a id="system-targets"></a>

## 系统目标

系统目标为可处理运行时间组件,这些部件使用 Orleans 信息,且不启动虚拟的单行排程。运行时间在明确的 silo 地址构建它们。会员服务、持续流动拉动代理和激活平衡协议使用系统目标,因为它们需要信息发送和排程模式,但决不能是地点透明的虚拟行为体。

与 grain不同的是,系统目标:

- 具有混凝土 silo 位置;
- 以运行时间代码创建和登记;
- 不通过放置或 grain-- -- 指令查询激活;
- 参加运行时间生命周期,而不是应用 grain 生命周期。

<a id="consistency-boundaries"></a>

## 一致性边界

Orleans 没有在这些子系统之间进行一项全球贸易。

- 成员名单将成员最新情况按单一顺序排列,逐个进行。
- 该目录协调 grain 身份,并配有启动地址和修理破旧登记。
- 激活调度器将同步工作项目序列成一个启动。
- 通信联系将请求与答复联系起来,但无法推断暂停请求是否得到执行。
- 持久性和溪流决定了它们本身的持久性和承认点。

延长运行时间时, 理解这些边界是关键。 自定义目录会改变位置一致性, 而不是会籍性。 职位安排主任会选择新激活开始的位置, 而不是如何排程。 流调整器会定义队列识别, 而不是 grain- 点名完全正经的语义 。

<a id="public-extension-surfaces"></a>

## 公共扩展表面

相对于取代内部运行时间类型而言,更倾向于支持的扩展点:

- <xref:Orleans.Runtime.Placement.IPlacementDirector> and placement filters customize candidate selection.
- <xref:Orleans.GrainDirectory.IGrainDirectory> supplies a named grain directory.
- 连环编码器、复印机、活性器和转换器可扩展电线处理。
- 溪流队列适配器、绘图器、平衡器、缓存和故障处理器会扩展持久性流。
- <xref:Orleans.Providers.IProviderBuilder`1> 整合配置驱动的供应商。
- 用户命令供应商启动和停业。

内部名称和算法不是兼容性的保障。 公共API、分析警告和有文件记录的电线身份是兼容性边界。
