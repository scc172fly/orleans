---
title: 开发 Orleans Grain
description: 在 Orleans 中定义 Grain 契约、实现 Grain 并调用它们。
ms.date: 08/21/2026
ms.topic: article
---

<a id="develop-orleans-grains"></a>

# 开发 Orleans Grain

Grain 是具有稳定逻辑标识的应用程序对象。Orleans 按需激活 Grain，将调用路由到其当前激活实例，并从内存中移除空闲激活实例。应用程序代码使用 Grain 引用，而无需构造 Grain 类或定位激活实例。

定义 Grain 契约或实现的项目应引用 [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk)。有关宿主和项目的设置，请参阅 [构建第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md#create-the-solution)。

<a id="define-a-grain-contract"></a>

## 定义 Grain 契约

Grain 接口继承一个 Grain 键接口，并声明异步方法：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="grain_interface":::

Orleans 支持以下 Grain 方法返回类型：

- <xref:System.Threading.Tasks.Task>
- <xref:System.Threading.Tasks.Task`1>
- <xref:System.Threading.Tasks.ValueTask>
- <xref:System.Threading.Tasks.ValueTask`1>
- <xref:System.Collections.Generic.IAsyncEnumerable`1>

对于无返回结果的方法使用 <xref:System.Threading.Tasks.Task> 或 <xref:System.Threading.Tasks.ValueTask>，对于有返回结果的方法使用其泛型形式。使用 <xref:System.Collections.Generic.IAsyncEnumerable`1> 实现 [响应流式传输](response-streaming.md)。不要在 Grain 契约中使用 `void`、`async void` 或同步返回类型。可以将 <xref:System.Threading.CancellationToken> 作为方法参数，以支持协作式取消。有关底层 C# 模型，请参阅 [异步编程](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/)。

参数、返回值和异常会跨越进程边界。应使应用程序数据能够由 Orleans 序列化，通常使用 <xref:Orleans.GenerateSerializerAttribute> 和稳定的 <xref:Orleans.IdAttribute> 值实现。Grain 引用本身已支持序列化，可以在调用中传递，也可以作为 Grain 状态的一部分存储。

<a id="implement-the-contract"></a>

## 实现契约

Grain 类通常继承 <xref:Orleans.Grain> 并实现一个或多个 Grain 接口：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="grain_implementation":::

Orleans 通过依赖注入创建 Grain 类。应用程序服务可以通过构造函数注入，而 <xref:Orleans.IGrainFactory> 可以通过 <xref:Orleans.Grain.GrainFactory?displayProperty=nameWithType> 获取。

> [!IMPORTANT]
> 每个 Grain 激活实例一次只执行一个轮次。不要使用 <xref:System.Threading.Tasks.Task`1.Result>、<xref:System.Threading.Tasks.Task.Wait*> 或 `GetAwaiter().GetResult()` 阻塞等待任务。应使用 `await`，以便 Orleans 能在激活实例的调度器上恢复请求执行。

<a id="get-and-call-a-grain-reference"></a>

## 获取和调用 Grain 引用

使用 <xref:Orleans.IGrainFactory.GetGrain*> 并传入接口及键：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_grain":::

获取引用不会创建或激活 Grain。首次需要激活实例的调用会促使 Orleans 放置并激活 Grain。如果激活实例迁移、停用或在另一个 Silo 上重新创建，该引用仍然有效。

<a id="understand-call-completion"></a>

## 理解调用完成的语义

常规 Grain 调用会以下列方式之一完成：

- 方法成功返回，可能带有返回结果。
- 方法抛出异常，异常传播至调用方。
- 调用方取消调用，并观察到 <xref:System.OperationCanceledException>。
- 调用方在响应超时之前未收到响应，并观察到 <xref:System.TimeoutException>。
- 消息传递或集群故障阻止调用完成。

超时仅告知调用方未及时收到响应，不能证明 Grain 方法没有运行或不会完成。<xref:Orleans.Configuration.MessagingOptions.CancelRequestOnTimeout> 默认为 `false`；启用后，Orleans 会在超时后尽力发送取消信号，而 Grain 仍必须通过观察取消令牌进行协作式取消。

在结果不确定之后，应用程序代码或基础设施可能重试分布式调用。如果重复执行会造成危害，应将操作设计为幂等操作。一种常见模式是携带操作 ID，并将已完成操作的 ID 与状态变更一同持久化。

在接口上配置方法级超时：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="response_timeout":::

全局默认值通过 <xref:Orleans.Configuration.ClientMessagingOptions> 和 <xref:Orleans.Configuration.SiloMessagingOptions> 配置。请参阅 [客户端配置](../host/configuration-guide/client-configuration.md)、[服务端配置](../host/configuration-guide/server-configuration.md) 和 [取消令牌](cancellation-tokens.md)。

<a id="activation-and-deactivation"></a>

## 激活与停用

当 Grain 需要在激活实例作用域内进行初始化或清理时，重写当前的生命周期方法：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activation_overrides":::

<xref:Orleans.Grain.OnActivateAsync*> 接受 <xref:System.Threading.CancellationToken> 参数，没有无参数重载。停用回调以尽力而为的方式执行；进程终止或某些故障发生后不会运行，因此不要依赖它们持久化关键状态。

有关回收、生命周期参与和迁移，请参阅 [Grain 生命周期](grain-lifecycle.md)。

<a id="test-grain-behavior"></a>

## 测试 Grain 行为

对大多数 Grain 测试使用 <xref:Orleans.TestingHost.InProcessTestCluster>，使激活、调度、序列化、依赖注入和消息传递均经过 Orleans 运行时。对提取出的应用程序逻辑使用常规单元测试。[测试 Orleans 应用程序](testing.md) 说明这些边界，包括适用于 [OrleansTestKit](https://github.com/OrleansContrib/OrleansTestKit) 的基础场景，并提供可执行的示例。有关完整的集群测试过程，请按照 [端到端测试 Orleans 应用程序](../tutorials-and-samples/testing-walkthrough.md) 操作。

<a id="choose-basic-or-advanced-features"></a>

## 选择基础或高级功能

大多数 Grain 只需要一个契约、一个实现、一个稳定的键以及常规的请求-响应调用。仅在工作负载确实需要时添加专门的行为：

- [请求调度与可重入性](request-scheduling.md)
- [使用 IAsyncEnumerable 进行响应流式传输](response-streaming.md)
- [Grain 定时器](timers.md)
- [提醒器](reminders.md)
- [实验性 Journaling 日志持久化](journaling/index.md)
- [观察者](observers.md)
- [Grain 放置](grain-placement.md)
- [无状态工作者 Grain](stateless-worker-grains.md)
- [Grain 调用筛选器](interceptors.md)
- [Grain 扩展](grain-extensions.md)
- [Grain 服务](grainservices.md)

[Orleans 运行时实现](../implementation/index.md) 文档介绍内部的调度、消息传递和生命周期组件。基础 Grain 开发不需要了解这些细节。
