---
title: "网络加固"
description: "限制客户 Orleans 、 silo、提供者和行政网络路径。"
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="network-hardening"></a>

# 网络加固

Orleans 部署使用单独的客户端到门户、 silo-至silo、供应商、应用程序发送器和行政网络路径。给每个路径它所需要的可达性。 端点配置告诉了 Orleans 倾听的地址和广告地址。 防火墙、网络政策和服务模件在这些终点周围强制执行可达性。

[地形和联网指南](../deployment/networking.md) 解释监听和广告的终点。在这些路径上应用以下安全政策:

| 终点或扶养 | 允许自 | 块块来自 |
|---|---|---|
| Silo 端口 |  属于同一预定组群的 Silos | Orleans 客户、公众侵犯和不相关的工作量 |
| Gateget 端口 | 核定 Orleans 客户工作量 | 浏览器、移动设备、公共互联网客户和不相关工作量 |
| 应用程序入侵 | 应用程序的预定调用者通过内盘控制器进行预定调用 | 绕过认证、授权或费率限制的直接路线 |
| 成员、储存、提醒和流源提供者 | 使用该供应商的工作量和身份 | 与无关联的集群、环境和公共网络,有私人进入 |
| 仪表板和诊断 | 行政途径的经授权经营者 | 公共和一般应用网络 |

Silos 直接与所有其他 silo的已公布地址通讯,所以路线 silo 终点直接联系。网关发现返回个别网关地址,客户网络必须到达已公布的终端。

<a id="harden-cluster-connectivity"></a>

## Harden Harden集群群组连连连

- 将 silo 和网关港口限制在核准的私人网络路径。
- 使用网络政策、供应商命名空间、账户和不同的集群身份,单独生产、集结、开发和非关联应用程序。
- 使用 `ServiceId` 和 `ClusterId` 进行路由和逻辑数据分离。使用网络政策和认证的运输身份入境。
- 限制对集群提供商的访问。一个能够读取发现数据的过程可以学习网关和 silo 地址;一个有写入权限的进程可以根据它能执行的供应商业务影响成员。
- 通过防火墙、网络地址翻译和服务模片,保护长寿命双向TCP连接。在重新连接、推出和 silo 替换过程中测试政策变化。
- 在公共应用程序边界应用连接、请求和资源限制,以约束经核准的客户所能开展的工作。

<a id="protect-traffic-in-transit"></a>

## 过境中保护性贩运

使用 [Orleans TLS 电信](../host/transport-layer-security.md) 来连接客户端对客户端对silo 和 silo-- -- 跨共享或不信任的网络的silo 连接。如果将工作量连接起来,都需要证明身份时,最好使用相互TLS。

Orleans TLS 保护 Orleans 配置 TLS 和服务器身份验证,用于集群、 grain 通过其服务客户的供应商连接,存储、提醒、流、遥测和其他供应商连接。 Orleans直接、长寿命长的TCP路径和广告刊登终点地形,同时保护交通。

服务器认证的 TLS 为客户设定了 silo 身份, 相互确定 TLS 既可以设定运输同行身份。 [应用程序认证和授权](authentication-authorization.md) 绘制这些身份和任何授权用户身份图,允许使用 grain 方法和租户。 狭义证书信托商店承认预期的集群身份 。

<a id="isolate-administrative-surfaces"></a>

## 孤立行政表面

HTTP 健康端点、 标准输出器、 特征分析工具和 Orleans 机头使用其主机堆的端点。 通过该堆站保护每个端点 。

<xref:Orleans.Dashboard.ServiceCollectionExtensions.MapOrleansDashboard*?displayProperty=nameWithType> 将仪表板绘制成 ASP.NET 核心请求管道。配置 HTTPS、操作员认证和授权以及该管道中的私人行政路径。 见 [安全的安全 Orleans 机首板](../dashboard/index.md#secure-the-dashboard-before-exposing-it)。

<a id="verify-the-deployed-boundary"></a>

## 核查部署的边界

测试允许和拒绝的路径:

1. 核定 silos 可达到每个公布的 silo 终点,政策堵塞了无关的工作量。
1. 核定客户可以进入广告网关,政策封锁公共和无关网络。
1. 东道主可以达到其角色所需的供应商终点。
1. TLS 端点拒绝不可信的证书、 无效名称和过期证书 。
1. 访问政策拒绝未经认证和未经授权的仪表板或诊断数据请求。
1. 推出、处理变化和连接重整保留政策执行。
在 [生产准备状态清单](../deployment/production-readiness.md)记录已部署的规则及其所有者。
