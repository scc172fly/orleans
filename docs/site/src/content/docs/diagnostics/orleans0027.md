---
title: "ORLEANS0027: Grain 接口成员从源中移除"
description: "理解和决心 ORLEANS0027 何时 OrleansContracts.txt 保留了源代码中不存在的 RPC 方法。"
ms.date: 08/28/2026
ms.topic: reference
---

<a id="orleans0027-grain-interface-member-removed-from-source"></a>

# ORLEANS0027: Grain 接口成员从源中移除

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

`OrleansContracts.txt` 声明 RPC 方法签名, 匹配源 grain 接口中缺少该签名。 结号前的值是线上的有效方法标识 Orleans 。

<a id="impact"></a>

## 影响影响

取消重电联方法可以中断老客户的电话或滚动升级期间的启动。 重生保留历史签名,这样电线合同的删除就仍然可见,需要明确决定。

<a id="how-to-fix"></a>

## 如何修补

当清除是意外时恢复源方法。当清除是故意的,审查混合版部署影响,酌情加插接口版本,并明确删除 `OrleansContracts.txt`中的保留签名。

见 [Orleans 合同兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有在有意与另一汇编合用时,才禁止。

```ini
[*]
dotnet_diagnostic.ORLEANS0027.severity = none
```
