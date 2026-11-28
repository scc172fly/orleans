---
title: "Orleans 日志持久化概述"
description: "了解用于持久化 Grain 值和集合的实验性 Orleans 日志持久化编程模型。"
ms.date: 08/21/2026
ms.topic: overview
---

<a id="orleans-journaling-overview"></a>

# Orleans 日志持久化概述

Orleans Journaling（日志持久化）是一种实验性持久化模型，将持久化值和集合的修改操作按顺序记录在每个 Grain 各自的日志中。Grain 激活时，Orleans 通过重放该日志重建其内存状态。一次写入可以持久化该 Grain 所拥有的多个持久化状态的变更。

> [!IMPORTANT]
> 包含 Journaling 的 `Microsoft.Orleans.Durable` 及其存储提供程序包是面向 .NET 11（`net11.0`）的 Alpha 预发布包。由于 API 和存储格式在稳定之前可能发生变化，评估这些包时应制定明确的升级、回滚、备份和恢复计划。

<a id="programming-model"></a>

## 编程模型

应用程序代码使用 <xref:Orleans.Durable.Journaling.IDurableStateManager>，在构造期间或同步激活设置期间声明具名持久化状态，并等待写入得到确认。标准管理器在 Grain 绑定的构造期间、依赖解析返回之前，将自身注册到生命周期中，因此已声明的状态会在应用程序激活及处理请求之前恢复。这既适用于普通 <xref:Orleans.Grain>，也适用于应用程序自己的 Grain 基类，或可复用的激活实例作用域功能组件。<xref:Orleans.Durable.Journaling.DurableGrain> 仍然是一个便利基类，公开同一个应用程序管理器，并提供受保护的写入辅助方法。键控依赖注入和编程方式访问解析到同一个具名状态对象。

Orleans 当前提供：

- <xref:Orleans.Durable.Journaling.IDurableValue`1>
- <xref:Orleans.Durable.Journaling.IDurableDictionary`2>
- <xref:Orleans.Durable.Journaling.IDurableList`1>
- <xref:Orleans.Durable.Journaling.IDurableQueue`1>
- <xref:Orleans.Durable.Journaling.IDurableSet`1>
- <xref:Orleans.Durable.Journaling.IDurableLog`1>，具有序列号、可流式传输草稿的仅追加日志
- <xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1>
- 由 Grain 日志提供后备存储的 <xref:Orleans.Runtime.IPersistentState`1>

在初始化前，通过 <xref:Orleans.Durable.Journaling.IDurableStateManager.GetOrAddState*> 或强类型的 <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions> 辅助方法声明状态。恢复完成后，调用会解析已有状态，应用程序方法即可使用其内容。初始化后尝试创建不存在的状态会立即失败。

修改操作更新激活实例的内存状态，并将编码后的操作添加到其待写入日志缓冲区中。在应用程序要求确保持久性的时点，等待 <xref:Orleans.Durable.Journaling.IDurableStateManager.WriteStateAsync*> 完成。返回的任务在存储提供程序确认追加或快照替换操作后完成。

将可能失败的工作先放在操作局部数据中准备，确保可以安全提交后再暂存修改操作。交错执行的调用共享待写入日志。日志操作失败时，管理器会被隔离，不再接受操作，并请求停用 Grain；新的激活实例会在恢复处理之前重建已持久化的结果。

每个具名状态在 Grain 日志中拥有稳定的流标识。跨部署保持这些名称稳定，以便恢复时将已存储的操作绑定到预期状态。

Grain 可以为其持久化值及日志公开监视流。日志写入得到确认后，流会投递变更；日志读取器也可以接收非持久化的草稿。参见[观察状态与流式草稿](observe-state.md)。

<a id="journal-lifecycle"></a>

## 日志生命周期

1. 解析 Grain 作用域内的管理器时，会将其注册到生命周期中。构造函数注入及共享设置会在生命周期启动前注册各个状态。
1. 在状态设置阶段，Orleans 按顺序读取日志，并在应用程序激活及处理请求前重放各个状态流。
1. Grain 代码在 Grain 的一次执行轮次内同步修改持久化值和集合。
1. `WriteStateAsync` 汇集管理器的待写入操作，向存储提交一次原子的日志追加或替换。
1. 达到配置的大小或行数阈值时，存储提供程序可以请求压实。下一次写入会创建当前持久化状态的快照，并以原子方式替换日志。
1. 后续激活实例通过重放最新快照及之后的操作恢复相同的持久化状态。

注册提供程序使这些服务可用；实际解析管理器的激活实例才会触发各 Grain 的日志 I/O。关于详细保证和调用方拥有的管理器生命周期，参见[运行时行为与一致性](runtime-behavior.md)。

<a id="journal-formats"></a>

## 日志格式

JSON Lines 是默认写入格式。每行包含一个状态流标识符和一个编码后的操作。使用裁剪或 Native AOT 时，为日志中的键、值和状态类型配置源生成的 <xref:System.Text.Json.Serialization.JsonSerializerContext> 元数据。

早期 Orleans 二进制格式仍然保持注册，以便部署能够读取已有数据。提供程序会随日志一起存储日志格式键。配置的写入格式与已存储格式不同时，恢复会使用存储格式对应的读取器；下一次写入则以配置的格式为日志创建快照。

关于格式及迁移，参见[配置 Journaling](configuration.md)。

<a id="journaling-and-event-sourcing"></a>

## 日志持久化与事件溯源

Orleans 提供两种独立的面向日志的编程模型：

| 模型 | 应用程序状态模型 | 持久化协调 |
| --- | --- | --- |
| Orleans Journaling | 通过 `IDurableStateManager` 与 Grain 或功能组件组合的可变持久化值和集合 | 每个 Grain 有一个由 `Microsoft.Orleans.Durable` 管理的日志 |
| [Orleans 事件溯源](../event-sourcing/index.md) | 将应用程序定义的事件应用于 `JournaledGrain<TState, TEvent>` | 日志一致性提供程序确认、持久化并同步事件 |

需要评估内置可变状态结构的基于操作的持久化方式时，选择 Journaling。应用程序需要领域事件、事件历史以及受支持的日志一致性编程模型时，选择事件溯源。

<a id="articles"></a>

## 相关文章

- [使用持久化状态](durable-state.md)
- [观察状态与流式草稿](observe-state.md)
- [运行时行为与一致性](runtime-behavior.md)
- [配置 Journaling](configuration.md)
- [Azure Storage 提供程序](azure-storage.md)
- [Redis 日志存储](redis-journal-storage.md)
- [Journaling 运维与故障排查](operations.md)
- [运行 Journaling 示例](samples.md)
