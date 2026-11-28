---
title: 异构 Orleans Silo
description: 在不同 Silo 上承载不同的 Orleans Grain 类型。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="heterogeneous-orleans-silos"></a>

# 异构 Orleans Silo

同一集群中的 Silo 可以承载不同的 Grain 类集合。这样既能隔离工作负载、使用专用硬件或独立部署 Grain 实现，又能保持单个 Orleans 集群。

:::image type="content" source="media/heterogeneous.png" alt-text="集群中的各个 Silo 支持不同的 Grain 类型集合。":::

所有 Silo 和客户端都应引用它们可能交换的接口与序列化契约。Silo 应仅引用自身能够承载的 Grain 实现程序集。

<a id="prefer-assembly-boundaries"></a>

## 优先使用程序集边界

最简单的方式是将专用 Grain 类放在独立项目中，仅由负责运行相应实现的 Silo 宿主引用各实现项目。Orleans 根据生成的类型元数据发现受支持的 Grain 类。

例如：

- 通用 Silo 引用 `Orders.Grains`。
- GPU Silo 引用 `Recommendations.Grains`。
- 客户端引用 `Orders.Contracts` 和 `Recommendations.Contracts`，但不引用这两个实现程序集中的任何一个。

所有公布支持某个 Grain 类型的 Silo 都必须使用彼此兼容的该 Grain 实现。

<a id="configuration"></a>

## 配置

当同一个二进制文件能够以多个角色运行时，配置 <xref:Orleans.Configuration.GrainTypeOptions.Classes>：

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="configure_grain_types":::

<xref:Orleans.Configuration.GrainTypeOptions.Classes> 是一组 <xref:System.Type> 值。使用它精确指定进程可承载的 Grain 类，或者移除某个角色不得承载的已发现类：

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="exclude_grain_type":::

不要使用 Orleans 早期版本中已过时的 Grain 类排除选项名称。

<a id="direct-placement-by-capability"></a>

## 按能力引导放置

当许多 Silo 加载相同的 Grain 实现，但只有部分 Silo 满足区域、硬件、租户或预留类型等放置要求时，使用 [Silo 元数据](configuration-guide/silo-metadata.md)和 [Grain 放置筛选](../grains/grain-placement-filtering.md)。

如果 Silo 完全无法承载某个实现，应使用异构 Grain 类型注册。如果它能够承载该类型，但放置需要优先匹配或强制匹配元数据，则使用放置筛选。

<a id="deployment-rules"></a>

## 部署规则

- 在所有角色之间保持 <xref:Orleans.Configuration.ClusterOptions.ServiceId>、<xref:Orleans.Configuration.ClusterOptions.ClusterId>、集群配置及协议配置一致。
- 在客户端调用某个受支持的 Grain 类型之前，至少部署一个支持该类型的健康 Silo。
- 分别维护每个专用 Grain 集合的容量与冗余。
- 先部署契约变更，再部署依赖这些变更的实现。
- 如果仍有请求或持久化工作以某个 Grain 类型为目标，应避免移除支持该类型的最后一个 Silo。

客户端在连接后获取集群类型信息。部署时应避免客户端在支持某个 Grain 类型的 Silo 可用之前依赖该类型。

<a id="limitations"></a>

## 限制

- 当没有活动 Silo 支持请求目标 Grain 类型时，请求会失败。
- 所有支持同一 Grain 类型的 Silo 都必须使用兼容的实现及契约。
- 无状态工作者 Grain 应在集群内一致可用，而不是被拆分为互不兼容的异构集合。
- 隐式流订阅要求兼容的 Grain 可用性；当异构部署导致归属关系不明确时，使用[显式订阅](../streaming/streams-programming-apis.md)。

使用接近生产环境的各角色实例数量测试拓扑变更，尤其是支持某个类型的最后一个 Silo 丢失及替换的情况。
