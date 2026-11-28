---
title: "ORLEANS0002: 不允许使用引用参数修饰符"
description: 了解并解决 Grain 接口方法使用 ref、out 或 in 参数时出现的 ORLEANS0002。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0002-reference-parameter-modifiers-are-not-allowed"></a>

# ORLEANS0002: 不允许使用引用参数修饰符

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

Orleans 的 Grain、观察者、扩展或系统目标接口上的实例方法使用了 `ref`、`out` 或 `in` 参数。

<a id="impact"></a>

## 影响

Orleans RPC 会跨进程边界序列化参数值，无法为远程调用提供本地引用或写回语义。

<a id="how-to-fix"></a>

## 修复方法

传递普通值，并通过异步结果返回输出。当方法需要返回多个值时，使用结果 DTO 或元组。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在已确认是误报，且该接口绝不会用作 Orleans 远程契约时抑制此诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0002.severity = none
```
