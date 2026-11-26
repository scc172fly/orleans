---
title: "请求的背景"
description: "流动应用元数据,调用 Orleans grain 。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="request-context"></a>

# 请求的背景

<xref:Orleans.Runtime.RequestContext> carries application metadata with an Orleans request. Typical values include correlation IDs, tenant IDs, and authorization context established by trusted application code.

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="set_request_context":::
接收方 grain 读取数值:

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="read_request_context":::
数值必须由 Orleans可序列。保持小值,因为 Orleans 将数值包含在请求信息中。

<a id="propagation"></a>

## 出生

请求使用 Async- 本地存储。 当代码发送 grain 呼叫时, Orleans 会复制当前在离线请求中的条目。 接收者 grain 会看到这些条目, 并会调用它来向上传播其当前的上下文 。

被调用者所做的更改不会在回复中回流。 请将请求背景视为下游元数据, 而不是回流渠道 。

使用静态 API 管理条目:

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="manage_request_context":::
尽可能地设置上下文,使其接近需要它的行动,并在不相关的行动在同一同步流动中执行之前恢复或明确价值。

<a id="security"></a>

## 安全安全安全安全安全安全安全

请求的背景是呼叫者提供的数据。 不要仅仅因为某个角色、用户身份或租户身份到达 `RequestContext`而信任该角色、用户身份或租户身份。 在可信赖的边界建立认证,并使用电话过滤器或应用程序授权逻辑验证访问权限。

身份传播和授权指导见 [和客户端 grain- 调用安全](../security/authentication-authorization.md) 。

<a id="placement-and-migration"></a>

## 安置和移徙

安装发生在新激活存在之前, 所以静态 `RequestContext` 不包含在布置主任和过滤器内。 读取 <xref:Orleans.Runtime.Placement.PlacementTarget.RequestContextData> 。

当 grain 使用 `MigrateOnIdle()`请求迁移时, Orleans 捕捉当前请求的背景并将其提供给安置。这允许申请提供安插提示,但自定义安插逻辑仍然是高级运行时间延展。

<a id="call-chain-reentrancy"></a>

## 呼叫链回转

<xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy> and `SuppressCallChainReentrancy` use request metadata internally to control scheduling for a call chain. Use their scoped return values with `using`; don't set Orleans-reserved context keys directly. See [Request scheduling](request-scheduling.md#call-chain-reentrancy).
