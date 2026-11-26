---
title: "背景服务和启动任务"
description: "与 Orleans运行应用程序初始化和背景工作。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="background-services-and-startup-tasks"></a>

# 背景服务和启动任务

使用标准 [.NET 托管服务](https://learn.microsoft.com/dotnet/core/extensions/workers) 来启动应用程序和背景工作。 Orleans 参加同一通用主机,因此登记令可以确保在托管服务开始之前就已准备好 Orleans 。

<a id="using-backgroundservice-recommended"></a>

<a id="using--recommended"></a>

## 使用 <xref:Microsoft.Extensions.Hosting.BackgroundService> (建议)

登记册 Orleans 首先,然后是 <xref:Microsoft.Extensions.Hosting.BackgroundService>:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="register_background_service":::

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="background_service":::

荣誉 `stoppingToken` ,这样服务不会延误 silo 关闭。直接使用 <xref:Microsoft.Extensions.Hosting.IHostedService> 进行一次性启动和关闭工作。

<a id="orleans-startup-tasks"></a>

## Orleans 启动任务

<a id="register-a-delegate"></a>

### 注册代表

使用 <xref:Orleans.Hosting.SiloBuilderStartupExtensions.AddStartupTask*> 当初始化必须在 Orleans 启动前生命周期阶段可以继续:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="register_startup_task":::

默认阶段是 <xref:Orleans.ServiceLifecycleStage.Active>。 例外情形失败了 silo 启动。 这适合于强制验证或初始化, 不适用于在主机准备好后可以重新尝试的可选工作 。

<a id="register-an-istartuptask-implementation"></a>

<a id="register-an--implementation"></a>

### 登记册 <xref:Orleans.Runtime.IStartupTask> 履行情况

对于可重复使用的任务,执行 <xref:Orleans.Runtime.IStartupTask>:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="validate_dependencies_task":::

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="register_validate_dependencies_task":::

<a id="choose-the-right-mechanism"></a>

## 选择正确的机制

<a id="using-ihostedservice"></a>

| 所需 | 机制机制机制 |
|---|---|
| 连续循环或排定的应用程序工作 | <xref:Microsoft.Extensions.Hosting.BackgroundService> |
| 一次性主机启动和关闭 | <xref:Microsoft.Extensions.Hosting.IHostedService> |
| 在特定 Orleans 阶段强制初始化 | <xref:Orleans.Hosting.SiloBuilderStartupExtensions.AddStartupTask*> |
| 启动并停止调回与 Orleans 子系统整合的回调 | <xref:Orleans.ILifecycleParticipant`1> 与 <xref:Orleans.Runtime.ISiloLifecycle> |

不要使用一个启动任务来进行长运行环路、多个复制品可以竞相应用的数据库迁移,或者需要不受约束的再处理的工作。协调外部迁移或使它们安全地成为单写者。

生命周期阶段的选择见 [Orleans silo 生命周期](../silo-lifecycle.md) 。
