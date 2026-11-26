---
title: "ORLEANS0019:已删除的 grain 接口未退出"
description: "理解和决心 ORLEANS0019 何时 OrleansContracts.txt 包含一个源不再定义的活跃界面。"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0019-removed-grain-interface-is-not-retired"></a>

# ORLEANS0019:已删除的 grain 接口未退出

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

`OrleansContracts.txt` 中的有效接口声明与任何已编译的 grain 界面不匹配。

<a id="impact"></a>

## 影响影响

删除或身份改变改名不作为故意而记录,旧的接口身份以后可以意外地重新使用。

<a id="how-to-fix"></a>

## 如何修补

如果删除接口是意外取消, 则恢复该界面。 否则应用 **标记为 *重新* 内 OrleansContracts.txt**保留退休声明作为合同历史。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单,并留存所有没有来源的申报单。为每个合同项目分别运行再生程序,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有当清单中有意含有另一编集拥有的合同时,才适宜禁止。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0019.severity = none
```
