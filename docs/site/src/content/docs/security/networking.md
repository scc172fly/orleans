---
title: 网络加固
description: 限制 Orleans 客户端、Silo、提供程序及管理网络路径。
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="network-hardening"></a>

# 网络加固

Orleans 部署分别使用客户端到网关、Silo 到 Silo、提供程序、应用入口及管理网络路径。为每条路径提供其所需的网络可达性。终结点配置告诉 Orleans 在哪里监听以及通告哪些地址；防火墙、网络策略及服务网格负责控制这些终结点的可达性。

[拓扑与网络指南](../deployment/networking.md) 说明了监听终结点和通告终结点。请对这些路径实施以下安全策略：

| 终结点或依赖项 | 允许访问的来源 | 阻止访问的来源 |
|---|---|---|
| Silo 端口 | 属于同一预期集群的 Silo | Orleans 客户端、公共入口及无关工作负载 |
| 网关端口 | 获准的 Orleans 客户端工作负载 | 浏览器、移动设备、公共互联网客户端及无关工作负载 |
| 应用入口 | 通过入口控制访问的预期应用调用方 | 绕过身份验证、授权或速率限制的直接路径 |
| 成员资格、存储、提醒器及流提供程序 | 使用该提供程序的工作负载及身份 | 无关集群、环境，以及可使用专用访问时的公共网络 |
| 仪表板及诊断 | 通过管理路径访问的获授权运维人员 | 公共网络及一般应用网络 |

Silo 直接与其他每个 Silo 的通告地址通信，因此必须直接路由到 Silo 终结点。网关发现会返回各个网关地址，因此客户端网络必须能够访问这些通告终结点。

<a id="harden-cluster-connectivity"></a>

## 加固集群连通性

- 将 Silo 和网关端口限制在获准的专用网络路径中。
- 通过网络策略、提供程序命名空间、帐户及不同的集群标识隔离生产、预发布、开发环境及无关应用。
- 使用 `ServiceId` 和 `ClusterId` 进行路由及逻辑数据隔离。使用网络策略和经过验证的传输身份进行准入控制。
- 限制对集群成员资格提供程序的访问。能够读取发现数据的进程可以获知网关和 Silo 地址；具有写入权限的进程则可以根据其能够执行的提供程序操作影响成员资格。
- 确保经过防火墙、网络地址转换及服务网格时，双向长连接 TCP 通信得以维持。测试重连、滚动发布及 Silo 替换期间的策略变更。
- 在公共应用边界实施连接、请求及资源限制，以约束获准客户端能够产生的工作量。

<a id="protect-traffic-in-transit"></a>

## 保护传输中的流量

对跨越共享或不可信网络的客户端到 Silo 连接以及 Silo 间连接使用 [Orleans TLS](../host/transport-layer-security.md)。当连接双方的工作负载均需要证书身份时，优先采用双向 TLS。

Orleans TLS 保护 Orleans 传输通道。通过各提供程序客户端为集群成员资格、Grain 存储、提醒器、流、遥测及其他提供程序连接配置 TLS 和服务器身份验证。兼容的服务网格在保护流量的同时，应保持 Orleans 的直接 TCP 长连接路径及通告终结点拓扑。

仅验证服务器身份的 TLS 向客户端确认 Silo 身份，双向 TLS 则确认两个传输对等方的身份。[应用身份验证与授权](authentication-authorization.md) 将这些身份及委托用户身份映射到获准的 Grain 方法和租户。通过范围受限的证书信任存储仅接纳预期集群的身份。

<a id="isolate-administrative-surfaces"></a>

## 隔离管理入口

HTTP 健康检查终结点、指标导出器、性能分析工具及 Orleans Dashboard 使用各自托管技术栈的终结点。通过该技术栈保护每个终结点。

<xref:Orleans.Dashboard.ServiceCollectionExtensions.MapOrleansDashboard*?displayProperty=nameWithType> 将仪表板映射到 ASP.NET Core 请求管道。在该管道中配置 HTTPS、运维人员身份验证与授权，以及专用管理访问路径。参阅[保护 Orleans Dashboard](../dashboard/index.md#secure-the-dashboard-before-exposing-it)。

<a id="verify-the-deployed-boundary"></a>

## 验证已部署的安全边界

从每个网络区域测试允许及拒绝的路径：

1. 获准的 Silo 能够访问每个通告的 Silo 终结点，且策略阻止无关工作负载访问。
1. 获准的客户端能够访问通告网关，且策略阻止公共网络和无关网络访问。
1. 宿主能够访问其角色所需的提供程序终结点。
1. TLS 对等方拒绝不受信任的证书、无效名称及已过期的证书。
1. 访问策略拒绝未通过身份验证或未经授权的仪表板及诊断数据请求。
1. 滚动发布、地址变更及连接重试不会破坏策略实施。
在[生产就绪检查清单](../deployment/production-readiness.md) 中记录已部署的规则及其负责人。
