---
title: "ORLEANS0018: Grain 接口成员未申报"
description: "理解和决心 ORLEANS0018 当 RPC 方法签名缺失时 OrleansContracts.txt."
ms.date: 08/28/2026
ms.topic: reference
---

<a id="orleans0018-grain-interface-member-not-declared"></a>

# ORLEANS0018: Grain 接口成员未申报

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

普通 grain- 界面方法在 `OrleansContracts.txt`中没有匹配的合同签名。方法识别、通用性、参数类型和顺序以及返回类型是签名的一部分,参数名称不是。

方法身份是源 `[Id]` 值,源代码 `[Alias]` 或生成的值 xxHash32 使用身份证使用的身份证 Orleans 代码生成器。 清单记录了结肠前的识别码, 而不取决于源是如何声明的 。

<a id="impact"></a>

## 影响影响

更老的启动程序可收到未知的RPC, 改变身份或有效载荷类型可能导致在滚动升级过程中发送或序列故障。

<a id="how-to-fix"></a>

## 如何修补

优先保留现有方法, 并添加新的改变行为的方法。 审查有效载荷兼容性, 酌情加增接口版本, 并应用 **添加到 OrleansContracts.txt**。代码将 CLR 签名和有效线条身份记录在舱单中。 源属性保持不变 。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单。为每个合同项目分别运行再生,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有在来源和表现为同一合同的情况下,仅对证明的签字正常化假正数加以禁止。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0018.severity = none
```

见 [后向兼容准则](../grains/grain-versioning/backward-compatibility-guidelines.md)。
