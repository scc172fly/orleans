---
title: "外部任务和 grain 时间安排"
description: "安全使用非同步图书馆、CPU工作,并阻止 Orleans grains的API。"
ms.date: 10/15/2026
ms.topic: concept-article
---

<a id="external-tasks-and-grain-scheduling"></a>

# 外部任务和 grain 时间安排

正常 `await` 在激活的转键列表模式中保留 grain 代码。 继续恢复在 grain 调度器上, 这样它就可以安全访问 grain 状态 :

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="refresh_from_repository":::
Async图书馆不需要 <xref:System.Threading.Tasks.Task.Run*>。直接等待它们。

<a id="dont-block-the-grain-scheduler"></a>

## 不要挡住 grain 排排程器

使用 <xref:System.Threading.Tasks.Task`1.Result>、 <xref:System.Threading.Tasks.Task.Wait*>、 <xref:System.Threading.Tasks.Task.WaitAll*>或 `GetAwaiter().GetResult()`,永远不要同步等待完成不完整的任务。堵塞可能会使激活陷入僵局,并使 .NET 线性集合断绝。

避免 `async void`,包括传递给API的单体羊羔 <xref:System.Action`1>。例外情况无法通过返回的任务来观察,而且可以终止过程。

<a id="use-taskrun-narrowly"></a>

## 缩略使用 Task.Run

<xref:System.Threading.Tasks.Task.Run*> 上 .NET 外的线形圆 Orleans 调度器。仅用于:

- 无法避免同步的屏蔽 API。
- 无法访问 grain 状态的CPU重工作 。

获取不可变的输入, 做外部工作, 然后在等待后更新 grain 状态 :

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="compress_on_thread_pool":::
不要读或变异 grain 字段内 <xref:System.Threading.Tasks.Task.Run*> 代表的代码不受 grain 排程器

## ConfigureAwait

不要用 `ConfigureAwait(false)` 直接在 grain 可在启动调度器之外恢复继续使用。 `ConfigureAwait(false)` 内部; grain 通常等待图书馆的任务时返回到调度器的排程程序代码。如果需要更多信息,请查看 .NET,则见 [ConfigureAwait 求求通](https://devblogs.microsoft.com/dotnet/configureawait-faq/).

<a id="start-activation-scheduled-work"></a>

## 启动启动计划的工作

<xref:System.Threading.Tasks.TaskFactory.StartNew*> 没有明确的调度器使用 <xref:System.Threading.Tasks.TaskScheduler.Current?displayProperty=nameWithType>,即 Orleans 调度器 grain 代码。这几乎是需要的;普通的自动同步方法更清楚。 .NET 时间安排上的差别,见 [Task.Run 和 Task.Factory.StartNew](https://devblogs.microsoft.com/dotnet/task-run-vs-task-factory-startnew/).

如果高级整合将一个助理代表传递到 <xref:System.Threading.Tasks.TaskFactory.StartNew*>,则卸载嵌套的任务:

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="start_async_worker":::
要将背景回调结果应用到 grain 状态, 请使用 <xref:Orleans.GrainBaseExtensions.RunRequestAsync*>。 它在激活时作为请求执行回调 。 见 [将背景工作作为 grain 请求运行](request-scheduling.md#run-background-work-as-a-grain-request)。

将背景工作置于一个观测运行时间机制之下。使用 [grain 计时器](timers.md)、 [催复提醒](reminders.md)、耐久的工作抽取或根据所需的寿命和可靠性提供托管服务。

<a id="reentrancy-still-applies"></a>

## 保留仍适用

等待外部工作时, 无法让其他请求运行在未重新输入 grain上。 标记一个 grain 重输入或促成中断可以改善传输量, 而呼叫等待 I/ O 时, 但状态可以在交替之间发生变化 。 见 [请求安排](request-scheduling.md)。
