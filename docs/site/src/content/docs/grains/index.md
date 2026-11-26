---
title: "开发 Orleans grains"
description: "界定 grain 合同,执行 grains,并在 Orleans中调用。"
ms.date: 08/21/2026
ms.topic: article
---

<a id="develop-orleans-grains"></a>

# 开发 Orleans grains

grain 是一个具有稳定逻辑特性的应用对象。 Orleans 需要时激活它,将电话转到当前启动,并从内存中去除闲置的激活。应用程序代码使用 grain 参考符而不是构建 grain 类或定位激活。

界定 grain 合同或执行参考文件 [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk)的项目。东道主和项目设置见 [构建您的第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md#create-the-solution)。

<a id="define-a-grain-contract"></a>

## 定义 grain 合同

grain 接口来自一个 grain 密钥界面,并宣布了非同步方法:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="grain_interface":::

Orleans 支持这些 grain 方法返回类型:

- <xref:System.Threading.Tasks.Task>
- <xref:System.Threading.Tasks.Task`1>
- <xref:System.Threading.Tasks.ValueTask>
- <xref:System.Threading.Tasks.ValueTask`1>
- <xref:System.Collections.Generic.IAsyncEnumerable`1>

使用 <xref:System.Threading.Tasks.Task> 或 <xref:System.Threading.Tasks.ValueTask> 方法的通用格式,以及这些方法的通用格式,返回结果。 <xref:System.Collections.Generic.IAsyncEnumerable`1> 用于 [响应流流](response-streaming.md). 不使用 `void`, `async void`,或同步返回类型 grain 合同合同 A <xref:System.Threading.CancellationToken> 可作为合作取消的方法参数列入。 [同步的方案拟订](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/).

参数、 返回值和例外跨进程边界。 使应用数据以 Orleans进行序列,通常使用 <xref:Orleans.GenerateSerializerAttribute> 和稳定的 <xref:Orleans.IdAttribute> 值。 Grain 参考文献已经可以序列化,可作为 grain 状态的一部分通过电话或存储。

<a id="implement-the-contract"></a>

## 执行合同

grain 类通常来自 <xref:Orleans.Grain> ,并安装一个或多个 grain 接口:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="grain_implementation":::

Orleans 通过依赖注射创建 grain 类。可提供建筑注射,用于申请服务, <xref:Orleans.IGrainFactory> 可通过 <xref:Orleans.Grain.GrainFactory?displayProperty=nameWithType>获得。

> [!IMPORTANT]
> grain 激活进程一次转一个圈。 不要用 <xref:System.Threading.Tasks.Task`1.Result>、 <xref:System.Threading.Tasks.Task.Wait*>或 `GetAwaiter().GetResult()`来阻拦任务。使用 `await` ,这样 Orleans 就可以在启动调度器上恢复请求。

<a id="get-and-call-a-grain-reference"></a>

## 获取并调用 grain 引用

使用 <xref:Orleans.IGrainFactory.GetGrain*> 接口和密钥:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="get_grain":::

获取引用时不会创建或激活 grain。第一个需要启动的呼叫会引发 Orleans 定位和激活它。如果激活动作、停止操作或重新生成到另一个 silo,该引用仍然有效。

<a id="understand-call-completion"></a>

## 理解呼叫完成

定期 grain 呼叫以下列一种方式完成:

- 方法返回成功, 可选择返回结果 。
- 方法抛出,例外则向打电话者传播。
- 打电话者取消电话,并观察 <xref:System.OperationCanceledException>。
- 电话在回复超时前没有收到回应, <xref:System.TimeoutException>.
- 通信或集束故障阻止了电话的通话。

超时通知呼叫者没有及时收到任何回复。 它不能证明 grain 方法没有运行或不会完成。 <xref:Orleans.Configuration.MessagingOptions.CancelRequestOnTimeout> 默认为 `false`; 启用后, Orleans 在超时后发出最努力的取消信号, 而 grain 仍然必须合作观察取消符号 。

分配的电话可以在结果不确定后通过应用代码或基础设施重新进行。设计操作在重复执行有害时是无能的。通常的模式是包括操作身份识别,并随着国家的变化持续填写完整的身份识别。

配置接口上每个方法的超时 :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="response_timeout":::

全球缺省通过 <xref:Orleans.Configuration.ClientMessagingOptions> 和 <xref:Orleans.Configuration.SiloMessagingOptions>配置。见 [客户配置](../host/configuration-guide/client-configuration.md)、 [服务器配置](../host/configuration-guide/server-configuration.md)和 [取消标记](cancellation-tokens.md)。

<a id="activation-and-deactivation"></a>

## 激活和停止作用

当 grain 需要启动-范围设置或清理时,覆盖当前生命周期方法:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activation_overrides":::

<xref:Orleans.Grain.OnActivateAsync*> 接受一个 <xref:System.Threading.CancellationToken>; 没有无参数超载。 取消激活回调是最好的努力, 不要在进程终止或某些失败后运行, 所以不要依赖它们来持续临界状态 。

收集、生命周期参与和移徙情况见 [Grain 生命周期](grain-lifecycle.md) 。

<a id="test-grain-behavior"></a>

## 测试 grain 行为

使用 <xref:Orleans.TestingHost.InProcessTestCluster> 测试大多数 grain 测试,这样可以激活、时间安排、序列、依赖性注射和消息传送到 Orleans 运行时间。使用普通单位测试来提取应用逻辑。 [测试 Orleans 应用](testing.md) 解释这些界限,包括适合 [OrleansTestKit](https://github.com/OrleansContrib/OrleansTestKit)的基本情景,并提供可执行的实例。跟踪 [测试 Orleans 应用程序的结束](../tutorials-and-samples/testing-walkthrough.md) ,以完整集束测试的通过。

<a id="choose-basic-or-advanced-features"></a>

## 选择基本或高级特征

多数 grains 只需要一份合同、一个执行、一个稳定的钥匙和定期的求援-答复电话。

- [请求时间安排和重发](request-scheduling.md)
- [与 IAsyncEnumerable相对应的流](response-streaming.md)
- [Grain 计时器](timers.md)
- [提醒提醒](reminders.md)
- [实验日志](journaling/index.md)
- [观察员观察员观察员观察员观察员观察员](observers.md)
- [Grain 安置](grain-placement.md)
- [无国籍工人 grains](stateless-worker-grains.md)
- [Grain 调用过滤器](interceptors.md)
- [Grain 扩展](grain-extensions.md)
- [Grain 服务](grainservices.md)

[Orleans 运行时执行](../implementation/index.md) 文件描述内部时间安排、信息传递和生命周期组成部分。这些细节对于基本的 grain 开发来说是不必要的。
