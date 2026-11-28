---
title: "ORLEANS0019: 已移除的 Grain 接口尚未标记为停用"
description: 了解并解决 OrleansContracts.txt 中包含源码不再定义的活动接口时出现的 ORLEANS0019。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0019-removed-grain-interface-is-not-retired"></a>

# ORLEANS0019: 已移除的 Grain 接口尚未标记为停用

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

`OrleansContracts.txt` 中的活动接口声明与任何已编译的 Grain 接口均不匹配。

<a id="impact"></a>

## 影响

删除或改变标识的重命名未被记录为有意变更，之后可能意外复用旧接口标识。

<a id="how-to-fix"></a>

## 修复方法

如果接口是意外移除的，请恢复接口；否则应用 **Mark as *RETIRED* in OrleansContracts.txt**。保留已停用的声明作为契约历史。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单，并将源码中不存在的所有声明标记为已停用。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅当清单明确包含由另一编译单元拥有的契约时才适合抑制。优先为每个项目使用单独的清单，使所有权保持明确。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0019.severity = none
```
