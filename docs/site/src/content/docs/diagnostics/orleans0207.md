---
title: "ORLEANS0207:使用不`$'的耐久操作名称"
description: "理解 ORLEANS0207, 其中报告一个 WithId 或 DurableTask.RunInline 名称, 内含“$” , 并通过选择另一个分隔符来修正它。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0207-use-durable-operation-names-without-"></a>

# ORLEANS0207:使用不`$'的耐久操作名称

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

耐久任务机构内收到的 `WithId` 或 `DurableTask.RunInline` 的常名包含 `$`。在客户和普通 grain 方法等持久任务机构之外, `WithId` 指一个根任务,其中可含有 `$`,因此规则只检查那里的 `DurableTask.RunInline` 名称:

:::code source="snippets/durable-tasks/Orleans0207.cs" id="orleans0207_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

运行时生成了包含 `$`的识别符号:未命名子和效果,如 `$child-1`;重复名称,如 `charge$2`;运行时间记录,如 `$signal:approved`。一个具有 `$` 的名称可以等于其中之一,因此 `WithId` 和 `DurableTask.RunInline` 在与 `ArgumentException`一起的耐久执行中拒绝该名称,任务完成后失败。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

选择一个没有 `$`的名称, 例如用 `-`分隔其部件的名称 :

:::code source="snippets/durable-tasks/Orleans0207.cs" id="orleans0207_fix" language="csharp":::

命名一个呼叫站点一次, 即使每次运行重复它。 运行时间数会重复发生本身, 所以等待 `.WithId("charge")` 的循环会创建 `charge`、 `charge$2`等。

<a id="when-to-suppress"></a>

## 何时压缩

不要抑制这种诊断: 运行时间拒绝这个名字。
