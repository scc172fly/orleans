---
title: "ORLEANS0007: 使用单个 Orleans 激活构造函数"
description: 了解旧版 ORLEANS0007 构造函数选择诊断。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0007-use-one-orleans-activation-constructor"></a>

# ORLEANS0007: 使用单个 Orleans 激活构造函数

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

此旧版规则表示 Orleans 序列化类型的构造函数选择存在歧义。当前 Orleans 激活机制最多选择一个构造函数用于生成式激活。

<a id="impact"></a>

## 影响

存在歧义的构造函数元数据会妨碍确定性的对象激活。旧的 `[OrleansConstructor]` 标记已过时，当前 Orleans 激活不再使用它。

<a id="how-to-fix"></a>

## 修复方法

移除过时或重复的构造函数标记。当生成式激活需要显式构造函数时，仅使用一个 `[GeneratedActivatorConstructor]`。

如果因构造函数声明带有无效的 `[GenerateSerializer]` 特性而出现此诊断，请移除这些构造函数级特性。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在检查所有构造函数标注后，确认诊断源自已知兼容性问题或测试产物时才抑制。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0007.severity = none
```
