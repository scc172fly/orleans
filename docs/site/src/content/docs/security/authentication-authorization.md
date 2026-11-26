---
title: "客户和 grain- 调用安全"
description: "在可信赖的边界线上验证呼叫者,并强制执行 Orleans grain 通话的申请授权。"
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="client-and-grain-call-security"></a>

# 客户和 grain- 调用安全

连接到网关后, Orleans 连接到 grain 申请应确定最终用户身份,创建任何 <xref:System.Security.Claims.ClaimsPrincipal> 政策使用,强制获取业务和资源。

<a id="keep-untrusted-callers-outside-the-orleans-boundary"></a>

## 将不信任的呼叫者留在 Orleans 边界之外

优先选择此请求路径 :

1. HTTP, gRPC,发文或其他应用程序端点认证调用者。
1. 端点验证输入并授权请求的应用操作。
1. 信任的主机代码调用所需的 grains 通过共同主持或通过外部 Orleans 客户端。
1. Grain 代码重复资源级检查,这种检查取决于 grain拥有的国家。

这一设计暴露了目的设计应用程序合同,并保留 Orleans 协议,并在工作量范围内引用 grain 接口。 使用网络政策,这样核准的应用工作量就可以到达网关港口。

对于信任程度较低的 Orleans 客户,请深入使用防御:限制其网络路径、用 [相互 TLS](../host/transport-layer-security.md#configure-mutual-tls)验证工作量、携带抗篡改应用程序证书并在每次受保护作业之前确认授权。界定认证的TLS工作身份图如何向 grain-调用身份和政策。

<a id="carry-identity-deliberately"></a>

## 故意携带身份

<xref:Orleans.Runtime.RequestContext> propagates serializable application metadata with a call and with downstream grain calls. Any Orleans client can set its values, so treat a user ID, role, tenant ID, or `"isAdmin"` flag as a caller-provided claim that requires validation.

安全模式包括:

- 在一个信任的应用边界验证, 并已经建立可信赖的代码为立即呼叫构建最小的身份背景 。
- a 携带在可信赖 silo 基础设施中签名、短期的证书并验证其签字、签发人、受众、寿命和具体业务索赔。
- 使用经认证的标的识别符号,确定来自可信赖商店的授权数据。

将原始密码和供应商证书保留在其秘密管理系统中。 携带最起码的、寿命短的身份元数据,并有明确的计划,在不相关的工作在同一同步流动之前清楚或取代请求背景。

请求的背景是临时传播,并带有上游呼叫者提供的数值。当呼叫跨越另一个应用信托边界时,更新身份,并通过中间 grains维护经验证的身份和租户价值。

<a id="enforce-authorization"></a>

## 强制授权

使用 <xref:Orleans.IIncomingGrainCallFilter> 来执行在 grain 或 grain-扩展方法之前必须运行的政策。一个 silo-- 全局过滤器可以强制执行共同基准;一个 grain 类可以实施与该 grain相关的政策接口。当授权失败时,过滤器拒绝拨号并在下一阶段前终止管道。

[grain 调用过滤器指导](../grains/interceptors.md#authorization-with-method-attributes) 显示政策属性和应用程序提供的身份访问器。在信任的基础设施之后,以该访问器为附件,验证了打电话者的证书。

授权应考虑业务和目标资源:

- 对照信任的 grain 状态检查租户或账户所有权,并使用 grain 键作为资源定位器。
- 将政策适用于能够到达受保护国的每一个接口或方法,包括 grain 扩展和行政方法。
- 当身份、租户、政策或要求的国家不存在时,即默认为拒绝。
- 保持政策评价的界限和可观察性。 silo-全局过滤器在其范围内的每一个调用上运行。
- 测试直接 grain 呼叫、下游 grain 呼叫、重试、其他 silos的呼叫以及公共端点。

`IGrainCallContext.SourceId` 将 Orleans 调用器识别为 grain 或客户实例。使用单独验证的应用身份来授权最终用户或工作量。 Grain 类型名称、 grain 键、 `ServiceId`和 `ClusterId` 确定路由和部署资源。

<a id="separate-transport-identity-from-application-authorization"></a>

## 申请授权时的单独运输身份

[TLS 电信](../host/transport-layer-security.md) 保护中转中的字节。服务器认证的 TLS为客户设定了 silo 身份,而相互确定 TLS 确定了两个工作量身份。应用程序政策绘制了这些身份图,允许使用 grain 方法、授权用户和租户资源。

当网络路径需要时使用两个层 :

- 网络政策和TLS决定哪些工作量可以建立 Orleans 连接。
- 应用程序认证可建立调用者或服务身份。
- Grain- 呼叫过滤器和 grain 逻辑授权操作和资源。

更广泛的边界模式见 [Orleans 安全](index.md)。
