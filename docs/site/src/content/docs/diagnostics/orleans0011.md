---
title: "ORLEANS0011: 重命名重复的 Alias"
description: 了解并解决 Orleans 别名冲突时出现的 ORLEANS0011。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0011-rename-a-duplicated-alias"></a>

# ORLEANS0011: 重命名重复的 Alias

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 部分可用 |

<a id="cause"></a>

## 原因

不同类型使用相同的非空 `[Alias]`，或者一个 Grain 接口中的多个方法使用同一别名。

<a id="impact"></a>

## 影响

冲突使类型注册、序列化标识或 RPC 方法分派产生歧义。

<a id="how-to-fix"></a>

## 修复方法

为每种序列化类型赋予全局唯一的稳定别名，为每个 Grain 接口方法赋予其接口内唯一的别名。

代码修复可以通过数字后缀重命名后续重复的方法别名。类型别名冲突需要有意进行手动重命名。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

不要抑制真实冲突。只有在确认这些标识不可能共存，并证实属于分析器作用域误报后，才可抑制。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0011.severity = none
```
