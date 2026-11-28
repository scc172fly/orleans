---
title: 外部任务与 Grain 调度
description: 了解如何在 Orleans Grain 中安全地使用异步库、CPU 密集型工作和阻塞式 API。
ms.date: 10/15/2026
ms.topic: concept-article
---

<a id="external-tasks-and-grain-scheduling"></a>

# 外部任务与 Grain 调度

普通的 `await` 使 Grain 代码保持在激活实例按执行轮次调度的模型中。后续执行会在 Grain 调度器上恢复，因此可以安全访问 Grain 状态：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="refresh_from_repository":::
异步库不需要使用 <xref:System.Threading.Tasks.Task.Run*>。直接等待它们返回的任务即可。

<a id="dont-block-the-grain-scheduler"></a>

## 不要阻塞 Grain 调度器

切勿使用 <xref:System.Threading.Tasks.Task`1.Result>、<xref:System.Threading.Tasks.Task.Wait*>、<xref:System.Threading.Tasks.Task.WaitAll*> 或 `GetAwaiter().GetResult()` 同步等待尚未完成的任务。阻塞可能使激活实例发生死锁，并耗尽 .NET 线程池的可用线程。

避免使用 `async void`，包括将异步 Lambda 表达式传给需要 <xref:System.Action`1> 的 API。通过返回的任务无法观察这些异常，异常还可能导致进程终止。

<a id="use-taskrun-narrowly"></a>

## 谨慎使用 Task.Run

<xref:System.Threading.Tasks.Task.Run*> 在 Orleans 调度器之外的 .NET 线程池上执行。仅将其用于：

- 无法避免的同步阻塞式 API。
- 不访问 Grain 状态的 CPU 密集型工作。

捕获不可变的输入，执行外部工作，然后在等待完成后更新 Grain 状态：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="compress_on_thread_pool":::
不要在 <xref:System.Threading.Tasks.Task.Run*> 委托内部读取或修改 Grain 字段。这部分代码不受 Grain 调度器保护。

## ConfigureAwait

不要直接在 Grain 方法中使用 `ConfigureAwait(false)`，它可能使后续执行在激活实例调度器之外恢复。通用库内部可以使用 `ConfigureAwait(false)`；Grain 代码以普通方式等待库返回的任务时，会回到自身的调度器。有关 .NET 上下文捕获的更多信息，请参阅 [ConfigureAwait 常见问题](https://devblogs.microsoft.com/dotnet/configureawait-faq/)。

<a id="start-activation-scheduled-work"></a>

## 启动由激活实例调度的工作

未显式指定调度器的 <xref:System.Threading.Tasks.TaskFactory.StartNew*> 使用 <xref:System.Threading.Tasks.TaskScheduler.Current?displayProperty=nameWithType>，在 Grain 代码中它就是 Orleans 调度器。很少需要这样做，普通异步方法更清晰。有关 .NET 调度差异，请参阅 [Task.Run 与 Task.Factory.StartNew](https://devblogs.microsoft.com/dotnet/task-run-vs-task-factory-startnew/)。

如果高级集成场景需要将异步委托传递给 <xref:System.Threading.Tasks.TaskFactory.StartNew*>，请展开嵌套任务：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="start_async_worker":::
要将后台回调的结果应用到 Grain 状态，请使用 <xref:Orleans.GrainBaseExtensions.RunRequestAsync*>。它将回调作为激活实例上的请求执行。参阅 [将后台工作作为 Grain 请求执行](request-scheduling.md#run-background-work-as-a-grain-request)。

应通过能够观察后台工作执行情况的运行时机制管理这些工作。根据所需的生存期与可靠性，选择 [Grain 定时器](timers.md)、[提醒器](reminders.md)、持久化作业抽象或托管服务。

<a id="reentrancy-still-applies"></a>

## 可重入规则仍然适用

等待外部工作并不会让非可重入 Grain 执行另一个请求。将 Grain 标记为可重入或启用交错执行，可以在调用等待 I/O 时提高吞吐量，但状态可能在不同执行轮次之间发生变化。参阅 [请求调度](request-scheduling.md)。
