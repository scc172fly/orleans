---
title: "ORLEANS0002:不允许引用参数修改器"
description: "理解和决心 ORLEANS0002 时 grain 方法界面使用参考、输出或参数。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0002-reference-parameter-modifiers-are-not-allowed"></a>

# ORLEANS0002:不允许引用参数修改器

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

一种 Orleans grain、观察者、扩展或系统目标接口使用 `ref`、 `out`或 `in` 参数的示例方法。

<a id="impact"></a>

## 影响影响

Orleans RPC 将参数值序列化为跨进程边界。 它无法为远程调用提供本地引用或回写语义。

<a id="how-to-fix"></a>

## 如何修补

通过非同步结果传递普通值和返回输出。 当方法返回多个值时, 使用结果 DTO 或 tuple 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对未经校验的假正值进行禁止, 如果接口从未用作 Orleans 远程合同。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0002.severity = none
```
