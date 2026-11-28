---
title: 后台服务与启动任务
description: 在 Orleans 中执行应用程序初始化和后台工作。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="background-services-and-startup-tasks"></a>

# 后台服务与启动任务

使用标准的 [.NET 托管服务](https://learn.microsoft.com/dotnet/core/extensions/workers) 执行应用程序初始化和后台工作。Orleans 参与同一个通用宿主，因此可以通过注册顺序确保 Orleans 在托管服务启动之前就绪。

<a id="using-backgroundservice-recommended"></a>

<a id="using-backgroundservice-recommended"></a>

<a id="using--recommended"></a>

## 使用 <xref:Microsoft.Extensions.Hosting.BackgroundService>（推荐）

首先注册 Orleans，然后注册 <xref:Microsoft.Extensions.Hosting.BackgroundService>：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="register_background_service":::

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="background_service":::

遵循 `stoppingToken`，以免服务延迟 Silo 关闭。对于一次性启动和关闭工作，可直接使用 <xref:Microsoft.Extensions.Hosting.IHostedService>。

<a id="orleans-startup-tasks"></a>

## Orleans 启动任务

<a id="register-a-delegate"></a>

### 注册委托

如果初始化必须在 Orleans 生命周期的某个阶段完成后才能继续启动，则使用 <xref:Orleans.Hosting.SiloBuilderStartupExtensions.AddStartupTask*>：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="register_startup_task":::

默认阶段为 <xref:Orleans.ServiceLifecycleStage.Active>。发生异常会使 Silo 启动失败。这适用于必需的验证或初始化，但不适用于可以在宿主就绪后重试的可选工作。

<a id="register-an-istartuptask-implementation"></a>

<a id="register-an-istartuptask-implementation"></a>

<a id="register-an--implementation"></a>

### 注册 <xref:Orleans.Runtime.IStartupTask> 的实现

对于可复用的任务，实现 <xref:Orleans.Runtime.IStartupTask>：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="validate_dependencies_task":::

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="register_validate_dependencies_task":::

<a id="choose-the-right-mechanism"></a>

## 选择合适的机制

<a id="using-ihostedservice"></a>

| 需求 | 机制 |
|---|---|
| 持续循环或定时执行的应用程序工作 | <xref:Microsoft.Extensions.Hosting.BackgroundService> |
| 一次性宿主启动和关闭工作 | <xref:Microsoft.Extensions.Hosting.IHostedService> |
| 在特定 Orleans 阶段完成的必需初始化 | <xref:Orleans.Hosting.SiloBuilderStartupExtensions.AddStartupTask*> |
| 与 Orleans 子系统集成的启动和停止回调 | <xref:Orleans.ILifecycleParticipant`1>，结合 <xref:Orleans.Runtime.ISiloLifecycle> 使用 |

不要将启动任务用于长时间运行的循环、多个副本可能竞争执行的数据库迁移，或需要无界重试的工作。在外部协调迁移，或确保迁移只能由单一写入者安全执行。

有关生命周期阶段选择，请参阅[Orleans Silo 生命周期](../silo-lifecycle.md)。
