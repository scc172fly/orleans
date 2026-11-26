---
title: "ORLEANS0007:使用一个 Orleans 激活构建器"
description: "了解遗产 ORLEANS0007 建筑选择诊断"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0007-use-one-orleans-activation-constructor"></a>

# ORLEANS0007:使用一个 Orleans 激活构建器

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

此遗留规则代表了对 Orleans- 序列型号的模糊构建器选择。当前 Orleans 激活使用,大多数是被选中用于生成激活的某个构建器。

<a id="impact"></a>

## 影响影响

结构元数据不清晰, 无法激活确定性物体。 旧的 `[OrleansConstructor]` 标记已经过时, 当前 Orleans 激活时没有使用 。

<a id="how-to-fix"></a>

## 如何修补

删除陈旧或重复的构建标记。当生成启动需要清晰的构造器时,请使用一个 `[GeneratedActivatorConstructor]`。

如果出现这种诊断是因为建筑申报含有无效的 `[GenerateSerializer]` 属性,则删除这些构建方层面的属性。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有在对所有建筑说明进行审查后,才禁止已知的兼容性或试验文物。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0007.severity = none
```
