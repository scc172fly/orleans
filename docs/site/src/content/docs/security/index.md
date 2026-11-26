---
title: "Orleans 安全"
description: "理解 Orleans 信任边界,并在 Orleans、应用程序和主机平台之间划分安全责任。"
ms.date: 08/17/2026
ms.topic: overview
---

<a id="orleans-security"></a>

# Orleans 安全

Orleans 提供运输安全、序列器类型控制以及 grain-呼叫过滤器,应用程序可以配置这些过滤器来强制执行政策。应用和托管平台验证用户和工作量,授权操作,保护网络路径和依赖关系,管理秘密,隔离租户。

每一个进入网关的流程都作为 Orleans 集集信任边界内的客户端客户端。 grain 合同可以产生 grain 已知引用 grain 并援引其界面方法。 grain 参考文献地址a grain申请授权允许进入其业务。

<a id="security-boundaries"></a>

## 安全边界

| 边界边界边界边界分界线 | Orleans 行为 | 应用或平台责任 |
|---|---|---|
| 公开侵犯应用程序主机 | HTTP、 gRPC和其他公共协议在 Orleans 调用开始之前提供以申请为目的的边界。 | 验证呼叫者、验证输入、费率限制请求和披露预定应用操作。 |
| Orleans 客户端端端口到 silo 网关 | 客户端发送 grain 电话打通的 Orleans 协议。 TLS 可以加密连接和验证工作量 。 | 限制工作量到达网关港口。 验证并批准在信任边界的通话 。 |
| Silo 至 silo | Silos 交换 grain 电话、会籍探测器、目录流量和运行时间协调。 TLS 可以保护 Orleans 连接 。 | 接纳预期的集群成员,隔离 silo 港口,保护证书信托和私人钥匙。 |
| 提供主机 | Orleans 供应商与成员、存储、提醒和流系统连接,使用具体供应商客户。 | 配置提供者认证、 TLS、 网络访问、 最小特权、 备份和审计等 。 |
| 板板和行政终端点 | 仪表板是一个核心端点 ASP.NET ,其访问政策遵循东道主的路径和中件配置。 | 要求操作者认证和授权、HTTPS和行政网络隔离。 |

<xref:Orleans.Configuration.ClusterOptions.ServiceId> and <xref:Orleans.Configuration.ClusterOptions.ClusterId> separate deployments logically. Provider access, network policy, and TLS identity control which processes can join or connect.

<a id="threat-model"></a>

## 威胁威胁模式

生产设计应说明:

- 未经授权的工作量到达网关、 silo、供应商或行政港口。
- 连接的 Orleans 客户端在用户授权范围之外引用了一种方法或 grain 密钥。
- 伪造的来电者提供的身份、角色或租户价值。
- 筛选出比应用程序预期的更多的氯乙烯单体类型的输入序列化。
- 所披露的全权证书、连接字符串、证书、 grain 状态或日志。
- 失密 silo、客户、供应商账户或证书,经给予该身份的所有许可后行事。
- 过度连接、电话、信息大小、热 grain 键或昂贵业务耗竭资源。

Grains 租户和租户 silo 分享其流程安全背景。 Grain 代码可以使用程序身份和为用户提供的服务 silo不同的流程、集群、账户和网络边界使工作量相互隔绝。

<a id="start-with-a-secure-design"></a>

## 以安全设计开始

1. 终止在应用控制HTTP、 gRPC或信息边界上的公共流量,将 silo 和网关限制为核定工作量。
1. 对该边界的调用者进行认证,并授权使用申请资源开展每次行动。 请 [grain 调用过滤器](authentication-authorization.md) 统一执行经验证的身份和政策。
1. 客户限制 silo、提供方和行政网络路径 [网络加固制导](networking.md).
1. 配置配置 [Orleans TLS 电信](../host/transport-layer-security.md) 当运输跨越共享或不信任的网络时。配置每个连接依赖性的提供者 TLS 。
1. 保持序列器类型表面的狭小度并验证已取消飞行的应用数据。见 [序列序列化安全](serialization.md)。
1. 使用工作量身份或受保护的秘密商店,给予每一个供应商最不享有的特权。 [机密或全权证书的机密和 全权证书的登记,以及全权证书的](secrets.md).
1. 将 [Orleans 机表](../dashboard/index.md) 作为行政终点保护。

安全控制在身份或政策数据缺失、无效或无法获取时应当关闭。 在允许生产运输之前,应实施认证失败、授权拒绝、证书轮换、证明过期和依赖隔离。

<a id="security-guidance-by-boundary"></a>

## 边界安全指导

- [客户和 grain- 调用安全](authentication-authorization.md)
- [逐步化安全](serialization.md)
- [机密和全权证书](secrets.md)
- [网络加固](networking.md)
- [与 TLS 连接的安全 Orleans](../host/transport-layer-security.md)
- [安全 Orleans 仪表板](../dashboard/index.md#secure-the-dashboard-before-exposing-it)
