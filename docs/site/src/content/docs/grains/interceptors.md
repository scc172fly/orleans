---
title: "Grain 调用过滤器"
description: "拦截进出 Orleans grain 电话。"
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="grain-call-filters"></a>

# Grain 调用过滤器

Grain 呼叫过滤器围绕 Orleans 电话运行。 用于交叉行为, 如授权、 遥测、 请求元数据、 故意翻译例外 。

- <xref:Orleans.IIncomingGrainCallFilter> runs on the target silo before and after a grain or extension method.
- <xref:Orleans.IOutgoingGrainCallFilter> runs on a client or silo that sends a call.

过滤器会形成一条不同步管道。 过滤器必须调用并等待 `context.Invoke()` 继续到下一个过滤器, 并最终达到目标方法 。

<a id="incoming-filters"></a>

## 接收过滤器

以下汇编的文件样本记录完成和失败:

:::code language="csharp" source="snippets/interceptors/LoggingFilters.cs" id="logging_incoming_filter":::

注册 silo:

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="register_incoming_filter":::

<a id="per-grain-filters"></a>

### 每一个grain 过滤器

grain 类可以执行 <xref:Orleans.IIncomingGrainCallFilter> ,过滤发送给该 grain 激活的电话,而不影响其他 grain 类:

:::code language="csharp" source="snippets/interceptors/GrainFilters.cs" id="per_grain_filter":::

过滤器将 `GetFavoriteNumber` 的结果从 `7` 改为 `38`。 A grain- 级别过滤器不需要建筑商登记,因为 Orleans 在 grain 实例中发现了它。它也通过激活来封存通过该激活发出的 grain 扩展电话,所以当行为不是要延期时,用 `InterfaceMethod` 或 `ImplementationMethod` 选择呼叫。

<a id="authorization-with-method-attributes"></a>

### 使用方法属性的授权

i-grain 过滤器可执行一个属性所声明的应用程序授权政策。此示例使用申请授权服务对认证的 <xref:System.Security.Claims.ClaimsPrincipal> 进行评估:

:::code language="csharp" source="snippets/interceptors/GrainFilters.cs" id="access_control_contract":::

grain 通过安装者注射接受受信任的身份存取和授权服务,然后检查属性,然后再继续拨打:

:::code language="csharp" source="snippets/interceptors/GrainFilters.cs" id="access_control_grain":::

属性在 grain 实施方法上, 因此过滤器可以从 `ImplementationMethod`读取。 在 grain 接口上设置属性, 如果该政策是公共合同的一部分, 则检查 `InterfaceMethod` 。

身份存取器必须在验证了打电话者的资质后,由可信赖的主机或过滤基础设施实施和容纳。 <xref:Orleans.Runtime.RequestContext> 是调用者可以设置的应用元数据,并会转而进入处理请求时发出的 grain 电话。将其价值视为不信任,除非在每一个信任边界上建立或确认其价值。一个 Orleans 客户可以设定一个 `"isAdmin"` 标志或用户名,因此这些值本身不是认证或授权边界。如果在请求中带有防篡改的证书,那么在产生经认证的本金之前,受信任的基础设施必须仍然予以验证。 `SourceId` 确定一个 Orleans 呼叫器,而不是一个经认证的最终用户。

完整的边界和执法模式见 [和客户端 grain- 调用安全](../security/authentication-authorization.md) 。

<a id="outgoing-filters"></a>

## 输出过滤器

输出过滤器使用相同的管道模式:

:::code language="csharp" source="snippets/interceptors/LoggingFilters.cs" id="logging_outgoing_filter":::

注册 silos 或客户端上的离线过滤器:

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="register_outgoing_filter":::

<a id="inspect-and-modify-calls"></a>

## 检查和修改电话

<xref:Orleans.IGrainCallContext> exposes:

- `SourceId` 和 `TargetId`。
- `InterfaceType`、 `InterfaceName`和 `MethodName`。
- `InterfaceMethod`.
- `Request`, an <xref:Orleans.Serialization.Invocation.IInvokable>.
- `Result` 和 `Response`。

所接收的环境还暴露 `TargetContext` 和 `ImplementationMethod`;即将发布的环境也暴露 `SourceContext`。

使用 `context.Request.GetArgumentCount()`、 `GetArgument(index)`和 `SetArgument(index, value)` 来检查或替换参数。当前背景和 `IInvokable` 不暴露一个 `Arguments` 数组 。

汇编的代表实例显示了请求的背景和结果修改:

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="silo_delegate_filter":::

改变论点或结果可能违反界面预期。 保留申报的种类,只对合同明确允许的方法进行转换。

<a id="call-grains-from-a-filter"></a>

## 从过滤器调用 grains

依赖注射容器创建了基于分类的过滤器,以便它们能够接收 <xref:Orleans.IGrainFactory> 并进行谨慎的审计或遥测电话:

:::code language="csharp" source="snippets/interceptors/GrainFactoryInjection.cs" id="grain_factory_injection":::

注册 silo 上的过滤器,以便 Orleans 能够构建和提供其依赖性:

:::code language="csharp" source="snippets/interceptors/Configuration.cs" id="register_grain_factory_filter":::

嵌套 grain 呼叫进入当前 silo 和目标 silo上的正常离线过滤管道。一个 silo-- 全方位的过滤器必须像样本那样排除审计目标,或者审计电话无限期地重新输入同一个过滤器和循环。使用明确的目标接口、 grain 类型、方法或标记保护,不要从接收的过滤器中调用请求的当前目标。

避免呼叫周期。 在过滤器等待嵌套电话时, 一个非回复周期 grain 仍然繁忙, 所以一个下游[ 将回拨回激活的 grain 可能会陷入僵局。 如果故意回召是不可避免的, 只对狭窄的呼叫链使用 <xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy> , 并记起连接; 见 [呼叫链回转](request-scheduling.md#call-chain-reentrancy)。 样本记录了原始呼叫前尝试过的尝试, 夫妇审计可提供请求的审计: 审计失败阻止了 `context.Invoke()` 运行。 审计和原始呼叫不是一次交易, 而且没有保证完全一次, 因此如果最初的呼叫后来失败或者应用级别重写可以产生重复, 记录可以保留。 只有在组合为有意时才使用等待的 grain 调来进行检查; 更愿意在观察工作上记录、 追踪或解开遥测 。

<a id="exceptions"></a>

## 例外例外例外例外

`context.Invoke()` 之前的代码正在进入呼叫中。 成功返回后将运行代码。 在等待的观察或转换失败的号召上使用 `try`、 `catch`和 `finally`

如果过滤器捕获了一个例外, 而不重新倒转, 则会处理该例外。 只有当调用者理解替换合同时才能替换例外。 Orleans 已经保存了无法使用的远程例外细节, 使用 <xref:Orleans.Serialization.UnavailableExceptionFallbackException>, 所以大范围例外转换是很少必要的 。

<a id="scope-and-ordering"></a>

## 范围和订购

接收过滤器还观察 grain 扩展呼叫。 发送过滤器可以观察 Orleans 系统呼叫, 除了应用程序呼叫之外。 如果行为不是全球范围的, 可以通过接口或方法过滤 。

登记册 silo- 具有 <xref:Orleans.Hosting.GrainCallFilterSiloBuilderExtensions.AddIncomingGrainCallFilter*>的全范围过滤器。类过滤器是 silo 服务提供商创建的单吨服务,其构建依赖性由该供应商解决。 Grain- 级过滤器拥有启动的 grain 寿命,通常的 grain 驱动器供应其构建依赖性。

输油管的输油量依此顺序:

1. Silo- 宽度过滤器,按登记顺序排列。
1. grain- 级别过滤器,如果 grain 实例执行 <xref:Orleans.IIncomingGrainCallFilter>。
1. 目标 grain 或 grain 扩展方法。

`context.Invoke()` 之前的代码遵循了该顺序。等待的呼叫后代码以反向顺序松开。

过滤器保持不同步、快速且没有阻断电话。 过滤器在其范围中为每通呼叫增加丁字裤,并可能成为整个集群的瓶颈。
