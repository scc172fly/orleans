---
title: "Orleans 日刊概览"
description: "理解实验 Orleans 将《公约》 grain 和收藏的数值。"
ms.date: 08/21/2026
ms.topic: overview
---

<a id="orleans-journaling-overview"></a>

# Orleans 日刊概览

Orleans 日记是一种实验性持久性模型,记录在每份grain 日记上与耐久值和收藏的突变。当一个 grain 启动时, Orleans 重播该日记以重建其模拟状态。单篇文字可以持续来自 grain拥有的多个耐久国的变化。

> [!IMPORTANT]
> `Microsoft.Orleans.Durable`,其中载有《日刊》,其储存提供者包件是前释放的阿尔法包件,目标是 .NET 11(`net11.0`)。 以明确的升级、回滚、备份和回收计划来评估它们,因为单价单和存储格式在稳定之前可以改变。

<a id="programming-model"></a>

## 方案拟订模式

应用代码使用 <xref:Orleans.Durable.Journaling.IDurableStateManager> 来宣布在建设或同步启动装置期间被命名的耐用状态,并承认其写作。标准管理员在 grain-受约束的建造过程中、在解析返回之前,注册生命周期,因此申报状态在申请激活和请求之前恢复。这与一个普通 <xref:Orleans.Grain>、应用程序所属 grain 基地或可重复使用的激活范围特征有关。 <xref:Orleans.Durable.Journaling.DurableGrain> 仍然是一个方便基础,暴露了同一应用程序管理员和保护性书写助手。键入式注射和方案访问解决了同名州标。

Orleans 目前规定:

- <xref:Orleans.Durable.Journaling.IDurableValue`1>
- <xref:Orleans.Durable.Journaling.IDurableDictionary`2>
- <xref:Orleans.Durable.Journaling.IDurableList`1>
- <xref:Orleans.Durable.Journaling.IDurableQueue`1>
- <xref:Orleans.Durable.Journaling.IDurableSet`1>
- <xref:Orleans.Durable.Journaling.IDurableLog`1>,仅附加的日志,带有序列号和流成草稿
- <xref:Orleans.Durable.Journaling.IDurableTaskCompletionSource`1>
- <xref:Orleans.Runtime.IPersistentState`1> 后 grain日记

声明在启动前有 <xref:Orleans.Durable.Journaling.IDurableStateManager.GetOrAddState*> 或输入的 <xref:Orleans.Durable.Journaling.DurableStateManagerExtensions> 帮助器。恢复后,调用解决现有状态,应用方法可以使用其内容。启动后创建缺失状态的请求立即失败 。

突变更新激活的模拟状态, 并在待处理日记缓冲中添加编码操作。 在应用程序耐久点等待 <xref:Orleans.Durable.Journaling.IDurableStateManager.WriteStateAsync*> 。 返回的任务在存储提供者确认附件或快照替换后完成 。

在当地数据操作中进行可编造的可编造的可编造工作,一旦安全发生变化,就逐步变异。
共享未完成日记账的调用。 失败的日记账操作将管理者围住,并要求 grain 停止使用;
重新激活程序在恢复处理之前重建持久结果。

每个被点名的国家在 grain 日记内都有稳定的流特性。 保持这些名称在部署中保持稳定, 这样回收就可以将存储的操作与预定状态捆绑在一起 。

Grains 可以曝光监视流, 显示其耐久值和日志。 串流在确认日记写作后会提供更改, 日志阅读器也可以接收波动草稿。 见 [观察州和河汇草案](observe-state.md)。

<a id="journal-lifecycle"></a>

## 期刊生命周期

1. 解决 grain- 范围管理器在生命周期启动前先将它注册到生命周期。 构造注入和共享设置登记系统都处于生命周期启动之前的位置 。
1. 在设置状态阶段, Orleans 按顺序读读日记,并在启动应用程序和请求之前重播每个州流。
1. Grain 代码同步变化, 在 grain 转弯
1. `WriteStateAsync` 收集经理待处理的业务,并提交一份原子日记附件或储存替换文件。
1. 存储提供者可以在配置大小或行阈值达到时请求压缩。 下一篇书可以创建当前耐久状态的快照, 原子取代日记 。
1. 稍后启动后重播最新快照和随后的行动,以恢复同样的持久性状态。

注册供应商可提供服务;启动程序解决经理每 --grain 日记 I/O触发的问题。 关于详细担保和电话管理人拥有的经理终身,见 [运行时行为和一致性](runtime-behavior.md)。

<a id="journal-formats"></a>

## 日记格式

JSON 线条是默认的写格式。 每行包含一个状态流标识符和一个编码操作。 配置源生成 <xref:System.Text.Json.Serialization.JsonSerializerContext> 用于使用剪辑或本地自动输出时的日记密钥、值和状态类型的元数据。

较早的 Orleans 二进制格式仍然被注册, 这样部署就可以读取现有数据。 供应商会将日记格式键与日记一起存储。 当配置的写入格式不同于所储存的格式时, 回收使用已保存的读者, 下次则以所配置的格式写入快照。

格式和移徙指南见 [配置日记](configuration.md) 。

<a id="journaling-and-event-sourcing"></a>

## 日刊和事件

Orleans 提供两种单独的面向日记账的方案编制模式:

| 型 型 | 应用国家模型 | 持久性协调 |
| --- | --- | --- |
| Orleans 日刊 | 可变耐久值和收藏,由 grain 或特性组成,通过 `IDurableStateManager` | 一人一个grain 由 `Microsoft.Orleans.Durable` |
| [Orleans 事件抛星](../event-sourcing/index.md) | 应用到 `JournaledGrain<TState, TEvent>` 的应用程序定义的事件  | 逻辑一致性提供者确认、持续和同步事件 |

在评价内建变异状态结构基于操作的持久性时选择日记。 当域事件、事件历史和所支持的对线一致性编程模式是应用要求时, 选择事件递增 。

<a id="articles"></a>

## 条款条款条款条款条款条款条条条条条条条条条条条条条

- [使用耐久状态](durable-state.md)
- [观察州和河汇草案](observe-state.md)
- [运行时行为和一致性](runtime-behavior.md)
- [配置日记](configuration.md)
- [Azure 储存供应商](azure-storage.md)
- [Redis 日记账存储](redis-journal-storage.md)
- [操作和排除故障](operations.md)
- [清点运行样本](samples.md)
