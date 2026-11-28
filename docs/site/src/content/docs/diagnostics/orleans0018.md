---
title: "ORLEANS0018: Grain 接口成员未声明"
description: 了解并解决 OrleansContracts.txt 中缺少 RPC 方法签名时出现的 ORLEANS0018。
ms.date: 08/28/2026
ms.topic: reference
---

<a id="orleans0018-grain-interface-member-not-declared"></a>

# ORLEANS0018: Grain 接口成员未声明

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

普通 Grain 接口方法在 `OrleansContracts.txt` 中没有匹配的契约签名。方法标识、泛型元数、参数类型及顺序和返回类型均属于签名；参数名称不属于签名。

方法标识是源码中的 `[Id]` 值、源码中的 `[Alias]` 值，或者 Orleans 代码生成器生成并使用的 xxHash32 ID。无论源码如何声明，清单都在冒号之前记录最终的标识符。

<a id="impact"></a>

## 影响

较旧的激活实例可能收到未知 RPC；标识或有效载荷类型的变更可能在滚动升级期间导致分派或序列化失败。

<a id="how-to-fix"></a>

## 修复方法

优先保留现有方法，并为变更后的行为添加新方法。检查有效载荷兼容性，在适当时递增接口版本，并应用 **Add to OrleansContracts.txt**。代码修复将 CLR 签名及有效的线协议标识记入清单，源码特性保持不变。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅当已证实为签名规范化误报，并且源码与清单表示同一契约时抑制。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0018.severity = none
```

参阅[向后兼容性指南](../grains/grain-versioning/backward-compatibility-guidelines.md)。
