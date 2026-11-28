---
title: 请求上下文
description: 随 Orleans Grain 调用传播应用程序元数据。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="request-context"></a>
# 请求上下文

<xref:Orleans.Runtime.RequestContext> 随 Orleans 请求携带应用程序元数据。常见值包括关联 ID、租户 ID，以及由受信任的应用程序代码建立的授权上下文。

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="set_request_context":::
接收请求的 Grain 读取该值：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="read_request_context":::
值必须能够被 Orleans 序列化。应尽量减小值的大小，因为 Orleans 会将它们包含在请求消息中。

<a id="propagation"></a>
## 传播

请求上下文使用异步局部存储。代码发起 Grain 调用时，Orleans 将当前条目复制到传出的请求中。接收请求的 Grain 可以读取这些条目，并且它发起的调用会继续向下游传播其当前上下文。

被调用方所做的更改不会随响应传回调用方。请将请求上下文视为向下游传播的元数据，而不是返回数据的通道。

使用静态 API 管理条目：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="manage_request_context":::
尽可能在接近实际需要上下文的操作处设置上下文；在同一异步执行流中的无关操作开始前，恢复或清除相应的值。

<a id="security"></a>
## 安全性

请求上下文是调用方提供的数据。不能仅仅因为角色、用户 ID 或租户 ID 来自 `RequestContext` 就信任它。应在可信边界进行身份验证，并使用调用筛选器或应用程序授权逻辑验证访问权限。

有关身份传播和授权的指导，请参阅[客户端与 Grain 调用的安全性](../security/authentication-authorization.md)。

<a id="placement-and-migration"></a>
## 放置与迁移

放置发生在新激活实例存在之前，因此放置决策器和筛选器中的静态 `RequestContext` 尚未填充。应改为读取 <xref:Orleans.Runtime.Placement.PlacementTarget.RequestContextData>。

当 Grain 使用 `MigrateOnIdle()` 请求迁移时，Orleans 会捕获当前请求上下文，并将其提供给放置机制。这样，应用程序就可以提供放置提示，但自定义放置逻辑仍属于高级运行时扩展。

<a id="call-chain-reentrancy"></a>
## 调用链可重入性

<xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy> 和 `SuppressCallChainReentrancy` 在内部使用请求元数据控制调用链的调度。应使用 `using` 管理它们返回的作用域对象，不要直接设置 Orleans 保留的上下文键。参阅[请求调度](request-scheduling.md#call-chain-reentrancy)。
