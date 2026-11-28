---
title: "ORLEANS0024: 已移除的 Grain 类尚未标记为停用"
description: 了解并解决 OrleansContracts.txt 中包含源码不再定义的活动 Grain 类时出现的 ORLEANS0024。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0024-removed-grain-class-is-not-retired"></a>

# ORLEANS0024: 已移除的 Grain 类尚未标记为停用

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

`OrleansContracts.txt` 中的活动类声明与任何已编译的具体 Grain 类均不匹配。

<a id="impact"></a>

## 影响

移除或改变标识的重命名未被记录，之后可能意外复用旧 Grain 标识。

<a id="how-to-fix"></a>

## 修复方法

如果该类是意外移除的，请恢复该类；否则应用 **Mark grain class as *RETIRED* in OrleansContracts.txt** 并保留声明。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单，并将源码中不存在的所有声明标记为已停用。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅当清单有意包含由另一编译单元拥有的类时抑制诊断。优先使用各项目独立的清单。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0024.severity = none
```
