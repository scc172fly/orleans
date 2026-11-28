---
title: "ORLEANS0207: 持久化操作名称不得包含 $"
description: 了解 ORLEANS0207：WithId 或 DurableTask.RunInline 的名称包含 $。选择其他分隔符来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0207-use-durable-operation-names-without-"></a>

# ORLEANS0207: 持久化操作名称不得包含 $

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体内部传给 `WithId` 或 `DurableTask.RunInline` 的常量名称包含 `$`。在持久化任务主体外部（例如客户端和普通 Grain 方法），`WithId` 为根任务命名，名称可以包含 `$`，所以规则在这些位置仅检查 `DurableTask.RunInline` 名称：

:::code source="snippets/durable-tasks/Orleans0207.cs" id="orleans0207_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

运行时会生成包含 `$` 的标识符：未命名的子任务和效果（如 `$child-1`）、重复名称（如 `charge$2`），以及运行时记录（如 `$signal:approved`）。含有 `$` 的名称可能与这些标识符相同，因此在持久化执行中，`WithId` 和 `DurableTask.RunInline` 会用 `ArgumentException` 拒绝该名称，任务在到达该操作时失败。

<a id="how-to-fix-violations"></a>

## 如何修复违规

选择不含 `$` 的名称，例如使用 `-` 分隔各部分：

:::code source="snippets/durable-tasks/Orleans0207.cs" id="orleans0207_fix" language="csharp":::

调用点只需命名一次，即使一次运行中重复执行该调用点。运行时自行给重复出现的操作编号，因此等待 `.WithId("charge")` 的循环会创建 `charge`、`charge$2` 等。

<a id="when-to-suppress"></a>

## 何时抑制

不要抑制此诊断：运行时会拒绝该名称。
