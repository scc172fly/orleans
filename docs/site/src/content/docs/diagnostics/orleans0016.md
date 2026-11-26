---
title: "ORLEANS0016: Grain 接口在 OrleansContracts.txt中不活跃"
description: "理解和决心 ORLEANS0016 时 grain 合同清单中缺少或退出接口。"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0016-grain-interface-is-not-active-in-orleanscontractstxt"></a>

# ORLEANS0016: Grain 接口在 OrleansContracts.txt中不活跃

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

合同分析师发现 grain 没有主动声明匹配其界面的 `GrainInterfaceType` 内 `OrleansContracts.txt`。匹配声明也可以存在,但可以标记 `*RETIRED*`.

<a id="impact"></a>

## 影响影响

界面身份、版本和方法在合同审查中不存在。 重命名或身份变更可以成为新的RPC合同,而没有明确的明确数据。

<a id="how-to-fix"></a>

## 如何修补

校验接口身份和版本, 然后应用 **添加到 OrleansContracts.txt**。代码修正会增加或重新启动界面并记录其普通程序方法 。

如果接口被意外恢复, 则从源代码中删除它或引入一个单独命名的替换, 而不是重复使用已取消的身份 。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单。为每个合同项目分别运行再生,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

如果项目故意不保持合同清单,则停止为项目进行合同分析,而不是压制个人接口。

```xml
<EnableOrleansContractsAnalyzer>false</EnableOrleansContractsAnalyzer>
```

见 [Orleans 合同兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md)。
