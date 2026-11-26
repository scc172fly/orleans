---
title: "ORLEANS0219:等待、合并或归还在册儿童的手柄"
description: "了解 ORLEANS0219,报告长期任务机构从未观察到的被安排在册儿童的处理情况,并通过等待处理、合并或拆卸工作来解决这个问题。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0219-await-combine-or-return-the-handles-of-scheduled-children"></a>

# ORLEANS0219:等待、合并或归还在册儿童的手柄

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 信息信息 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

长期任务机构将一名随附子女 `ScheduleAsync` 丢弃回的手柄,或将其持有在当地人手中,因为当地人的唯一用途是读取其特性,例如 `Id`:

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

在持久任务机构, `ScheduleAsync` 开始一个随身儿童,一旦儿童的宿主接受并储存了孩子,就返回其手柄。处决即为儿童所有:死刑取消到达时,并删除处决树,一旦处决结束,则取消仍在运行的儿童。儿童的结果和失败只达到手柄的代码。

以此为例, 通知仍可运行时身体会完整。 通知若不成功, 发出命令时没有工作流量失败的记录, 而当执行树被删除时仍然在运行的通知则被取消 。

附加启动和分离启动,见 [开始和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**等待把手。** 儿童在身体继续时运行,在身体完成前观察其结果:

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_fix_await" language="csharp":::

**将控点传给组合器。** `ScheduledTask.WhenAll` 等待每个控点,然后按输入顺序丢弃第一个失败:

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_fix_combinator" language="csharp":::

**把手还给我** 返回把手的物体将观察结果传给其呼叫者。

**完成的分解工作比执行期长** `Detached()` 开始独立根任务, 执行完成后和树被删除后继续 :

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_fix_detached" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

禁止工作流量忽视结果的儿童诊断,如果删除行刑树,这些结果可能会停止,例如最佳的缓冲温度。
