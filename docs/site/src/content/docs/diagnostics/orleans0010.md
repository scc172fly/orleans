---
title: "ORLEANS0010: 添加稳定的 Alias"
description: 了解并解决 Orleans 类型或 RPC 方法使用名称派生标识时出现的 ORLEANS0010。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0010-add-a-stable-alias"></a>

# ORLEANS0010: 添加稳定的 Alias

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 信息 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

Grain 接口、Grain 接口方法或使用生成式序列化器的数据类型缺少 `[Alias]`。

<a id="impact"></a>

## 影响

Orleans 会回退到根据名称派生的标识。重命名 CLR 类型或方法可能破坏已序列化的数据、RPC 兼容性或滚动升级。

<a id="how-to-fix"></a>

## 修复方法

添加有意选定的稳定别名，并在 CLR 重命名时保留该别名。代码修复会根据命名空间和嵌套关系生成类型别名，或根据当前方法名称生成方法别名。

检查为重载方法生成的别名，因为每个重载都需要唯一的方法标识。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在明确不关心重命名、部署及持久化数据之间的兼容性时抑制诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0010.severity = none
```
