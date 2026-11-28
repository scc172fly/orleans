---
title: 机密与凭据
description: 保护 Orleans 提供程序凭据、工作负载身份、证书及敏感配置。
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="secrets-and-credentials"></a>

# 机密与凭据

Orleans 将 .NET 配置中的凭据提供给各提供程序的专用客户端。托管平台的工作负载身份和机密管理设施负责颁发、存储及轮换这些凭据。按照各集群成员资格、存储、提醒器、流和遥测提供程序的凭据指南进行配置。

<a id="prefer-workload-identity"></a>

## 优先使用工作负载身份

如果提供程序支持，使用工作负载身份或其他短期凭据机制。例如，Azure 提供程序通常接受 <xref:Azure.Core.TokenCredential>；生产工作负载可以使用 <xref:Azure.Identity.ManagedIdentityCredential> 进行基于身份的访问。对于其他平台的等效机制，请遵循提供程序文档。

向每个工作负载授予其角色所需的数据平面操作权限：

- 当所选集群成员资格提供程序要求时，客户端需要访问网关发现数据。
- Silo 需要成员资格操作权限及其使用的特定存储、提醒器、流和目录操作权限。
- 部署与架构管理身份可以与运行时身份分离。
- 生产、预发布和开发环境应使用独立的身份及提供程序命名空间。

将 <xref:Orleans.Configuration.ClusterOptions.ServiceId>、<xref:Orleans.Configuration.ClusterOptions.ClusterId>、Grain 键、提供程序名称及连接终结点视为标识符。通过经过身份验证的提供程序连接及网络策略保护访问。

<a id="handle-unavoidable-secrets"></a>

## 处理无法避免的机密

当提供程序要求连接字符串、密码、访问密钥或证书密码时：

- 从获准的机密提供程序或受保护的部署配置中加载。
- 将读取权限限制在工作负载身份及负责轮换的运维人员范围内。
- 通过旨在限制进程检查和诊断信息暴露的受保护配置通道传递机密。
- 对日志、跟踪、指标、异常消息、健康检查响应及仪表板可见状态中的凭据信息进行脱敏。
- 将 Grain 键和请求上下文限制为允许披露的标识符及应用元数据。

[.NET 配置系统](https://learn.microsoft.com/dotnet/core/extensions/configuration) 会组合多个配置提供程序。检查最终优先级，并确保生产机密来源具有预期的优先级。通过编程式提供程序配置传递 SDK 凭据对象。

<a id="protect-certificates-and-private-keys"></a>

## 保护证书与私钥

TLS 证书是工作负载凭据。将私钥访问权限限制在进程身份范围内，使用适合工作负载角色的名称及扩展密钥用途，并在部署支持时为各工作负载颁发独立证书。

证书的加载、缓存、释放、刷新及信任存储管理属于应用和平台的责任。为颁发者证书及叶证书的轮换安排重叠有效期，监控到期时间并测试重连行为。参阅[加载和轮换 Orleans TLS 证书](../host/transport-layer-security.md#load-the-local-certificate)。

<a id="design-rotation-and-failure-behavior"></a>

## 设计轮换与故障处理行为

对于每项凭据：

1. 记录其负责人、权限、来源、使用方、到期时间及轮换过程。
1. 确定提供程序 SDK 是否自动刷新凭据，或者宿主是否必须重新连接或重启。
1. 在凭据到期前及身份验证失败时发出告警，同时对凭据脱敏。
1. 在流量运行期间测试轮换，并验证旧凭据在重叠有效期结束后被撤销。
1. 决定凭据提供程序故障时是拒绝新工作、保留现有连接，还是将宿主标记为未就绪。

将凭据轮换与定期授权审查相结合。验证当前提供程序身份仍遵循最小权限原则，并确认已撤销权限的客户端、Silo 和运维人员无法访问。
有关凭据和提供程序连接的网络边界，请参阅[网络加固](networking.md)。
