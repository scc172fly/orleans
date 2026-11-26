---
title: "ORLEANS0024:已删除的 grain 类没有退休"
description: "理解和决心 ORLEANS0024 何时 OrleansContracts.txt 包含活动 grain 类别源不再定义。"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0024-removed-grain-class-is-not-retired"></a>

# ORLEANS0024:已删除的 grain 类没有退休

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

`OrleansContracts.txt` 中的活动类声明不符合任何已编译的混凝土 grain 类。

<a id="impact"></a>

## 影响影响

转移或身份改变改名不记录,旧的 grain 身份以后可以意外重新使用。

<a id="how-to-fix"></a>

## 如何修补

如果删除是意外取消, 恢复该类。 否则应用 **标记标记标记 grain 类作为 *重新* 内 OrleansContracts.txt** 并保留声明。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单,并留存所有没有来源的申报单。为每个合同项目分别运行再生程序,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有当舱单故意包括另一编集拥有的类别时,才禁止。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0024.severity = none
```
