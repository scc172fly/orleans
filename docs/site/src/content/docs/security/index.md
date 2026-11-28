---
title: Orleans 安全
description: 了解 Orleans 的信任边界，并在 Orleans、应用及托管平台之间划分安全责任。
ms.date: 08/17/2026
ms.topic: overview
---

<a id="orleans-security"></a>

# Orleans 安全

Orleans 提供传输安全、序列化器类型控制及 Grain 调用筛选器，应用可以配置这些机制来实施策略。应用和托管平台负责验证用户及工作负载身份、授权操作、保护网络路径及依赖项、管理机密并隔离租户。

每个能够访问网关的进程都作为集群信任边界内的 Orleans 客户端运行。具备兼容 Grain 契约的已连接客户端可以根据已知 Grain 类型及键创建 Grain 引用，并调用其接口方法。Grain 引用用于寻址 Grain，应用授权则授予访问其操作的权限。

<a id="security-boundaries"></a>

## 安全边界

| 边界 | Orleans 的行为 | 应用或平台的责任 |
|---|---|---|
| 应用宿主的公共入口 | HTTP、gRPC 等公共协议在 Orleans 调用开始之前提供面向应用的边界。 | 验证调用方身份、验证输入、限制请求速率并公开预期的应用操作。 |
| Orleans 客户端到 Silo 网关 | 客户端通过 Orleans 协议发送 Grain 调用。TLS 可以加密连接并验证工作负载身份。 | 限制能够访问网关端口的工作负载，在可信边界进行身份验证和调用授权。 |
| Silo 到 Silo | Silo 之间交换 Grain 调用、成员资格探测、目录通信及运行时协调信息。TLS 可以保护 Orleans 连接。 | 仅接纳预期集群的成员，隔离 Silo 端口，并保护证书信任配置及私钥。 |
| 宿主到提供程序 | Orleans 提供程序通过专用客户端连接成员资格、存储、提醒器及流系统。 | 配置提供程序身份验证、TLS、网络访问、最小权限、备份及审计。 |
| 仪表板和管理终结点 | 仪表板是 ASP.NET Core 终结点，其访问策略遵循宿主的路由及中间件配置。 | 要求运维人员通过身份验证和授权，使用 HTTPS，并隔离管理网络。 |

<xref:Orleans.Configuration.ClusterOptions.ServiceId> 和 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 在逻辑上区分部署。提供程序访问权限、网络策略及 TLS 身份控制哪些进程可以加入或连接。

<a id="threat-model"></a>

## 威胁模型

生产环境设计应考虑：

- 未经授权的工作负载能够访问网关、Silo、提供程序或管理端口。
- 已连接的 Orleans 客户端调用其用户授权范围之外的方法或 Grain 键。
- 调用方提供的身份、角色或租户值被伪造。
- 序列化输入选择了超出应用预期范围的 CLR 类型。
- 凭据、连接字符串、证书、Grain 状态或日志被泄露。
- 遭到入侵的 Silo、客户端、提供程序帐户或证书能够以相应身份拥有的全部权限执行操作。
- 过多的连接、调用、消息大小、热点 Grain 键或高开销操作导致资源耗尽。

同一 Silo 中的 Grain 和租户共享该进程的安全上下文。Grain 代码可以使用 Silo 可用的进程身份及服务。通过独立进程、集群、帐户及网络边界实现工作负载之间的隔离。

<a id="start-with-a-secure-design"></a>

## 从安全设计着手

1. 让公共流量在应用控制的 HTTP、gRPC 或消息传递边界终止，并仅允许获准的工作负载访问 Silo 和网关。
1. 在该边界验证调用方身份，并根据应用资源对每项操作进行授权。使用 [Grain 调用筛选器](authentication-authorization.md) 一致地实施已验证的身份与策略。
1. 按照[网络加固指南](networking.md) 限制客户端、Silo、提供程序及管理网络路径。
1. 传输跨越共享或不可信网络时，配置 [Orleans TLS](../host/transport-layer-security.md)。为每项依赖连接配置提供程序 TLS。
1. 缩小序列化器可接受的类型范围，并验证反序列化后的应用数据。参阅[序列化安全](serialization.md)。
1. 使用工作负载身份或受保护的机密存储，并为每个提供程序身份授予最小权限。参阅[机密与凭据](secrets.md)。
1. 将 [Orleans Dashboard](../dashboard/index.md) 作为管理终结点加以保护。

身份或策略数据缺失、无效或不可用时，安全控制应默认拒绝访问。在接纳生产流量之前，测试身份验证失败、授权拒绝、证书轮换、凭据过期及依赖项隔离等情况。

<a id="security-guidance-by-boundary"></a>

## 按边界划分的安全指南

- [客户端与 Grain 调用安全](authentication-authorization.md)
- [序列化安全](serialization.md)
- [机密与凭据](secrets.md)
- [网络加固](networking.md)
- [使用 TLS 保护 Orleans 连接](../host/transport-layer-security.md)
- [保护 Orleans Dashboard](../dashboard/index.md#secure-the-dashboard-before-exposing-it)
