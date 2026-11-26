---
title: "为什么 Orleans"
description: "了解 Orleans 虚拟行为体模式的好处和利弊权衡。"
ms.date: 08/08/2026
ms.topic: overview
---

<a id="why-orleans"></a>

# 为什么 Orleans

Orleans 帮助 .NET 开发商建立分布式、有声的应用程序,将虚拟行为者编程模式与定位、信息传递、生命周期和故障检测等运行时间服务结合起来。

其主要好处不是分配变得无形。 网络电话可能失败,必须配置储存,工作量仍然需要能力规划。 Orleans 减少处理和协调大量独立实体所需的具体应用基础设施数量。

<a id="stable-identities-instead-of-locations"></a>

## 稳定身份而不是地点

应用程序代码通过接口和密钥地址 grains 。 Orleans 映射激活的逻辑身份和线路呼叫。调用者不保留服务器注册或当放置改变时重新创建引用。

由于身份独立于激活, Orleans 可以按需启动 grains ,从记忆中去除闲置的激活。应用程序代表的逻辑实体远多于同时适合存储的实体。

[Orleans 虚拟行为者研究](https://www.microsoft.com/research/project/orleans-virtual-actors/) 将这一数值与虚拟内存作比较:应用程序代码可以处理一个 grains 的大逻辑空间,而运行时间决定哪些激活是当前常住和在哪里启动。

<a id="isolated-turn-based-execution"></a>

## 孤立、按转手执行

每个激活 grain 都包含其行为和状态。 默认情况下, 它会一次处理一个请求。 这样每个实体的变量比共享模拟同值货币更容易理解, 并消除了对典型 grain 代码锁定的需要 。

边界是明确的: grain 电话是无序的,可能跨越一个进程或机器。这鼓励了包含延迟、序列化、取消和故障的API。

<a id="natural-partitioning"></a>

## 自然分割

将域实体绘图到 grains 分区,按身份状态和工作。 Orleans 可在一个集群和线路通话中放置这些启动装置,而电话人不知道它们的位置。

当负荷分布在许多 grain 密钥上时,这一设计最有效。热 grain 仍是一个热 grain: Orleans 不会自动复制普通状态 grain 来增加其输送量。应用程序应该选择 grain 边界、无国籍工人、聚合等级或其他与工作量相适应的模式。

<a id="managed-activation-lifecycle"></a>

## 管理下的激活生命周期

Orleans 激活 grains 当他们接到电话并能够停止闲置启动以恢复记忆时,可以停止这些闲置激活。 silo 失败, 稍后的电话可以重新启动 grains 健康、健康和健康 silos 成员会聚之后。

激活恢复与州立复制不同。 此过程会失去多变的记忆状态。 持久恢复需要配置好的存储提供者和成功呼叫持久性API。

<a id="composable-runtime-services"></a>

## 可复合运行时服务

Orleans 为下列目的提供一致的主机和编程抽象:

- 集群成员和客户发现。
- 启动安置和重新平衡。
- Grain 持续和交易。
- 计时器、提醒和持久工作。
- 流和广播频道。
- 序列化、版本化、安保和可观察性。

提供方包将这些抽象内容与基础设施(如 Azure 存储、 Azure 宇宙DB、关系数据库、 DynamoDB、 Redis、事件枢纽、SQS、NATS、领事、Cassandra、 ZooKeeper和 Kubernetes)等基础设施整合。

<a id="familiar-net-development"></a>

## 熟悉 .NET 开发

Grain 合同是与非同步方法的 .NET 接口, grain 实施是分类,主机使用 .NET 通用主机和依赖注射。 Orleans 还结合了 ASP.NET 核心、记录、配置、 OpenTelemetry、“信使”、标准测试工具。

结果是,在确定地点、生命周期和运行时间而不是应用管道时,一个分布式方案拟订模式仍然可以识别 .NET 。

<a id="tradeoffs"></a>

## 权衡取舍

Orleans 并非最适合于每项工作量。当应用主要需要:

- 各个组成部分共有的可变内存。
- 几个非常平行的大型计算工作
- 根据每项要求进行全球协调。
- 实体身份价值很小的散装数据处理。

对于合适的域, Orleans 提供了一个有生产力的默认架构。 它不会去除分布式系统之间的取舍, 但给应用程序一个测试的基础, 用来解决这些问题 。
