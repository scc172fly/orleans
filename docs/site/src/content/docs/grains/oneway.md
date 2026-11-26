---
title: "单行 grain 调用"
description: "使用单向请求,以获取最佳努力 Orleans 通知。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="one-way-grain-calls"></a>

# 单行 grain 调用

常规 grain 调用返回一个在 Orleans 收到方法响应时完成的任务。该任务包含一个返回值或例外,并告诉呼叫者请求已完成。

一种方法,标记为 <xref:Orleans.Concurrency.OneWayAttribute> 返回后返回呼叫器 Orleans 接受发送请求。 呼叫者没有收到完成信号、 结果或例外:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="one_way_audit":::
单程方法必须返回 <xref:System.Threading.Tasks.Task> 或 <xref:System.Threading.Tasks.ValueTask>,而不是其通用形式。

<a id="delivery-semantics"></a>

## 交付语义

单向电话是最佳努力:

- 完成返回的任务并不意味着收到或处理请求的目标。
- 目标的例外不归回呼叫者
- 呼叫者无法区分成功处理与信息丢失或目标失败。
- 取消和反应超时语义不能提供有用的完成保证 因为没有回应

仅使用单向电话通知安全丢失且不需要结果的状态。 遥测提示、缓存失效提示或冗余状态信号可以适合此模式 。

不要使用单向电话要求国家改变,而这种改变需要确认、财务业务、工作流程过渡或打电话者可能需要重新尝试的任何操作。 最好使用常规请求- 响应呼叫、耐久的队列或者在交付和回收事项时使用 Orleans 流动提供者。

单向电话可以减少响应信息管理费用,但可以作为先进的优化。 在替换常规电话之前先采取措施。
