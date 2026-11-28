---
title: "ORLEANS0027: Grain 接口成员已从源码移除"
description: 了解并解决 OrleansContracts.txt 保留了源码中不存在的 RPC 方法时出现的 ORLEANS0027。
ms.date: 08/28/2026
ms.topic: reference
---

<a id="orleans0027-grain-interface-member-removed-from-source"></a>

# ORLEANS0027: Grain 接口成员已从源码移除

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

`OrleansContracts.txt` 声明了一个在匹配的源码 Grain 接口中不存在的 RPC 方法签名。冒号之前的值是 Orleans 在线协议上使用的有效方法标识。

<a id="impact"></a>

## 影响

移除 RPC 方法可能在滚动升级期间破坏较旧客户端或激活实例发出的调用。重新生成会保留历史签名，使线协议契约的移除保持可见，并需要作出明确决定。

<a id="how-to-fix"></a>

## 修复方法

如果是意外移除，请恢复源码方法。如果是有意移除，请检查对混合版本部署的影响，适当时递增接口版本，并显式从 `OrleansContracts.txt` 中移除保留的签名。

参阅[Orleans 契约兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

接受契约移除后，优先更新经过审查的清单。仅在清单有意与另一编译单元共享时抑制诊断。

```ini
[*]
dotnet_diagnostic.ORLEANS0027.severity = none
```
