---
title: 客户端与 Grain 调用安全
description: 在可信边界验证调用方身份，并对 Orleans Grain 调用实施应用级授权。
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="client-and-grain-call-security"></a>

# 客户端与 Grain 调用安全

连接网关会建立用于 Grain 调用的 Orleans 传输连接。应用负责确定最终用户身份、构造策略所需的 <xref:System.Security.Claims.ClaimsPrincipal>，并控制对操作和资源的访问。

<a id="keep-untrusted-callers-outside-the-orleans-boundary"></a>

## 将不可信调用方隔离在 Orleans 边界之外

优先采用以下请求路径：

1. HTTP、gRPC、消息传递或其他应用终结点验证调用方身份。
1. 终结点验证输入，并对请求的应用操作进行授权。
1. 可信宿主代码通过共同托管的或外部的 Orleans 客户端调用所需的 Grain。
1. Grain 代码再次执行依赖于该 Grain 所拥有状态的资源级检查。

此设计对外公开专门设计的应用契约，将 Orleans 协议及所引用的 Grain 接口限制在工作负载边界之内。使用网络策略，仅允许获准的应用工作负载访问网关端口。

对于部署在较低信任层的 Orleans 客户端，采用纵深防御：限制其网络路径，使用[双向 TLS](../host/transport-layer-security.md#configure-mutual-tls) 验证工作负载身份，携带防篡改的应用凭据，并在每项受保护操作之前验证授权。明确经过 TLS 验证的工作负载身份如何映射到 Grain 调用身份及策略。

<a id="carry-identity-deliberately"></a>

## 有意识地传递身份信息

<xref:Orleans.Runtime.RequestContext> 随调用及后续 Grain 调用传播可序列化的应用元数据。任何 Orleans 客户端都可以设置其中的值，因此应将用户 ID、角色、租户 ID 或 `"isAdmin"` 标志视为调用方提供的、需要验证的声明。

安全做法包括：

- 在可信应用边界完成身份验证，由可信代码为当前调用构造最小化的身份上下文。
- 携带已签名、短期有效的凭据，并在可信 Silo 基础设施中验证其签名、颁发者、受众、有效期及操作专用声明。
- 使用已验证身份的主体标识符，从可信存储中获取授权数据。

将原始密码和提供程序凭据保存在各自的机密管理系统中。只传递具有明确架构的最少短期身份元数据，并在同一异步执行流开始无关工作之前清除或替换请求上下文。

请求上下文具有传递性，其中的值来自上游调用方。当调用跨越另一个应用信任边界时，重新验证身份；经过中间 Grain 时保留已验证的身份及租户值。

<a id="enforce-authorization"></a>

## 实施授权

对于必须在 Grain 或 Grain 扩展方法之前执行的策略，使用 <xref:Orleans.IIncomingGrainCallFilter>。Silo 级筛选器可以实施通用基线策略；Grain 类可以实现该接口，以执行该 Grain 专有的策略。授权失败时，筛选器拒绝调用，并在进入下一阶段之前终止管道。

[Grain 调用筛选器指南](../grains/interceptors.md#authorization-with-method-attributes) 演示了策略特性及由应用提供的身份访问器。在可信基础设施验证调用方凭据后，填充该访问器。

授权应同时考虑操作及目标资源：

- 根据可信 Grain 状态检查租户或帐户归属关系，并将 Grain 键用作资源定位符。
- 对能够访问受保护状态的每个接口或方法实施策略，包括 Grain 扩展和管理方法。
- 缺少身份、租户、策略或所需状态时，默认拒绝。
- 限制策略评估的资源开销，并使其可观测。Silo 级筛选器会对其作用范围内的每次调用执行。
- 测试直接 Grain 调用、下游 Grain 调用、重试、来自其他 Silo 的调用以及公共终结点。

`IGrainCallContext.SourceId` 将 Orleans 调用方标识为 Grain 或客户端实例。应使用另外验证过的应用身份来授权最终用户或工作负载。Grain 类型名称、Grain 键、`ServiceId` 和 `ClusterId` 用于标识路由和部署资源。

<a id="separate-transport-identity-from-application-authorization"></a>

## 将传输身份与应用授权分离

[TLS](../host/transport-layer-security.md) 保护传输中的字节。仅验证服务器身份的 TLS 向客户端确认 Silo 的身份，而双向 TLS 确认双方工作负载的身份。应用策略将这些身份映射到获准的 Grain 方法、委托用户和租户资源。

在网络路径需要时，同时使用这两个层次：

- 网络策略与 TLS 决定哪些工作负载能够建立 Orleans 连接。
- 应用身份验证确定调用方或服务的身份。
- Grain 调用筛选器与 Grain 逻辑对操作和资源进行授权。

有关更完整的边界模型，请参阅 [Orleans 安全](index.md)。
