---
title: Grain 调用筛选器
description: 拦截传入和传出的 Orleans Grain 调用。
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="grain-call-filters"></a>

# Grain 调用筛选器

Grain 调用筛选器围绕 Orleans 调用执行。可使用筛选器实现授权、遥测、请求元数据和有意的异常转换等横切功能。

- <xref:Orleans.IIncomingGrainCallFilter> 在目标 Silo 上运行，包围 Grain 或扩展方法的执行。
- <xref:Orleans.IOutgoingGrainCallFilter> 在发送调用的客户端或 Silo 上运行。

筛选器构成异步管道。筛选器必须调用并等待 `context.Invoke()`，才能继续执行下一个筛选器并最终调用目标方法。

<a id="incoming-filters"></a>

## 传入调用筛选器

以下经过编译验证的文档示例会记录调用完成和失败的情况：

:::code language="csharp" source="snippets/interceptors/LoggingFilters.cs" id="logging_incoming_filter":::

在 Silo 上注册此筛选器：

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="register_incoming_filter":::

<a id="per-grain-filters"></a>

### 单个 Grain 的筛选器

Grain 类可以实现 <xref:Orleans.IIncomingGrainCallFilter>，以筛选分派到该 Grain 激活实例的调用，而不影响其他 Grain 类：

:::code language="csharp" source="snippets/interceptors/GrainFilters.cs" id="per_grain_filter":::

此筛选器将 `GetFavoriteNumber` 的结果从 `7` 改为 `38`。Grain 级筛选器无需在构建器中注册，因为 Orleans 会从 Grain 实例上发现它。它也会包围经由该激活实例分派的 Grain 扩展调用，因此，如果该行为不适用于扩展方法，请使用 `InterfaceMethod` 或 `ImplementationMethod` 选择适用的调用。

<a id="authorization-with-method-attributes"></a>

### 使用方法特性进行授权

单个 Grain 的筛选器可以强制执行由特性声明的应用程序授权策略。此示例使用应用程序授权服务评估已经过身份验证的 <xref:System.Security.Claims.ClaimsPrincipal>：

:::code language="csharp" source="snippets/interceptors/GrainFilters.cs" id="access_control_contract":::

Grain 通过构造函数注入接收可信的身份访问器和授权服务，然后在继续调用之前检查相应特性：

:::code language="csharp" source="snippets/interceptors/GrainFilters.cs" id="access_control_grain":::

特性位于 Grain 实现方法上，因此筛选器从 `ImplementationMethod` 读取特性。如果策略属于公共契约的一部分，则应将特性放在 Grain 接口上，改为检查 `InterfaceMethod`。

身份访问器必须由可信的宿主或筛选器基础设施实现，并在验证调用方的凭据之后由该基础设施填充。<xref:Orleans.Runtime.RequestContext> 是调用方可以设置的应用程序元数据，它会传递到处理请求期间发起的后续 Grain 调用。除非可信基础设施在每个信任边界设置或验证这些值，否则应视其为不可信。Orleans 客户端可以自行设置 `"isAdmin"` 标志或声称的用户 ID，因此仅凭这些值不能构成身份验证或授权边界。如果请求上下文携带抗篡改的凭据，可信基础设施仍必须先验证它，再生成经过身份验证的主体。`SourceId` 标识 Orleans 调用方，而不是经过身份验证的最终用户。

有关完整的边界和强制执行模型，请参阅[客户端与 Grain 调用安全性](../security/authentication-authorization.md)。

<a id="outgoing-filters"></a>

## 传出调用筛选器

传出调用筛选器使用相同的管道模式：

:::code language="csharp" source="snippets/interceptors/LoggingFilters.cs" id="logging_outgoing_filter":::

在 Silo 或客户端上注册传出调用筛选器：

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="register_outgoing_filter":::

<a id="inspect-and-modify-calls"></a>

## 检查和修改调用

<xref:Orleans.IGrainCallContext> 公开以下成员：

- `SourceId` 和 `TargetId`。
- `InterfaceType`、`InterfaceName` 和 `MethodName`。
- `InterfaceMethod`。
- `Request`（一个 <xref:Orleans.Serialization.Invocation.IInvokable>）。
- `Result` 和 `Response`。

传入调用上下文还公开 `TargetContext` 和 `ImplementationMethod`；传出调用上下文公开 `SourceContext`。

使用 `context.Request.GetArgumentCount()`、`GetArgument(index)` 和 `SetArgument(index, value)` 检查或替换参数。当前的上下文及 `IInvokable` 不公开 `Arguments` 数组。

以下经过编译验证的委托示例演示了如何修改请求上下文及返回结果：

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="silo_delegate_filter":::

修改参数或返回结果可能违反接口契约的预期。应保留声明的类型，并且只对契约明确允许此类转换的方法应用转换。

<a id="call-grains-from-a-filter"></a>

## 在筛选器中调用 Grain

基于类的筛选器由依赖注入容器创建，因此可以接收 <xref:Orleans.IGrainFactory>，并发起带有防递归保护的审计或遥测调用：

:::code language="csharp" source="snippets/interceptors/GrainFactoryInjection.cs" id="grain_factory_injection":::

在 Silo 上注册筛选器，使 Orleans 能够构造它并提供其依赖项：

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="register_grain_factory_filter":::

嵌套 Grain 调用会进入当前 Silo 的正常传出调用筛选器管道，以及目标 Silo 的传入调用筛选器管道。Silo 范围的传入调用筛选器必须像示例一样排除审计目标，否则审计调用会重新进入同一个筛选器并无限递归。应使用明确的目标接口、Grain 类型、方法或标记作为保护条件，并且不要从当前请求的传入调用筛选器中调用该请求本身的目标 Grain。

避免循环调用。不可重入 Grain 在其筛选器等待嵌套调用时仍处于忙碌状态，因此如果下游 Grain 回调该激活实例，可能发生死锁。如果不可避免地需要有意回调，仅在狭窄的调用链范围内使用 <xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy>，并考虑交错执行；参见[调用链可重入性](request-scheduling.md#call-chain-reentrancy)。示例在原始调用之前记录尝试，并将审计服务的可用性与请求耦合：如果审计失败，`context.Invoke()` 就不会运行。审计与原始调用不属于同一个事务，也不保证恰好一次，因此原始调用之后失败时可能留下审计记录，应用层重试也可能产生重复记录。仅在有意建立这种耦合时才等待 Grain 调用；对于观测性工作，优先使用日志记录、跟踪或解耦的遥测。

<a id="exceptions"></a>

## 异常

`context.Invoke()` 之前的代码在进入调用时执行，其后的代码在调用成功返回时执行。使用 `try`、`catch` 和 `finally` 包围等待的调用，以观察或转换故障。

如果筛选器捕获异常而不重新引发，则视为异常已得到处理。只有调用方了解替换后的异常契约时才应替换异常。对于本地不可用的远程异常类型，Orleans 已使用 <xref:Orleans.Serialization.UnavailableExceptionFallbackException> 保留其异常详情，因此通常不必进行广泛的异常转换。

<a id="scope-and-ordering"></a>

## 作用范围和顺序

传入调用筛选器也会观察到 Grain 扩展调用。传出调用筛选器除应用调用外，还可能观察到 Orleans 系统调用。如果行为并非全局适用，则按接口或方法进行筛选。

使用 <xref:Orleans.Hosting.GrainCallFilterSiloBuilderExtensions.AddIncomingGrainCallFilter*> 注册 Silo 范围的筛选器。基于类的筛选器是由 Silo 服务提供程序创建的单例服务，其构造函数依赖项从该提供程序解析。Grain 级筛选器的生命周期与 Grain 激活实例相同，由常规 Grain 激活器提供构造函数依赖项。

传入调用管道按以下顺序运行：

1. Silo 范围的筛选器，按注册顺序执行。
1. Grain 级筛选器（如果 Grain 实例实现了 <xref:Orleans.IIncomingGrainCallFilter>）。
1. 目标 Grain 方法或 Grain 扩展方法。

`context.Invoke()` 之前的代码按上述顺序执行。等待的调用返回后，后续代码按相反顺序逐层执行。

筛选器应保持异步、快速，且不得执行阻塞调用。筛选器会增加其作用范围内每次调用的延迟，并可能成为整个集群的瓶颈。
