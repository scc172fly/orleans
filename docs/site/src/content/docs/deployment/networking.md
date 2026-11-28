---
title: 拓扑、网络与集群
description: 为生产环境配置 Orleans 终结点、网络路径与集群发现。
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="topology-networking-and-clustering"></a>

# 拓扑、网络与集群

Orleans 部署有三条不同的网络通信路径：

| 路径 | 用途 | 必需的网络可达性 |
| --- | --- | --- |
| Silo 到 Silo | 成员资格探测、Grain 调用、目录通信及运行时协调 | 每个 Silo 均能访问所有已通告的 Silo 终结点 |
| 客户端到网关 | Orleans 客户端发起的 Grain 调用 | 每个客户端均能访问其发现的已通告网关终结点 |
| 主机到提供程序 | 成员资格、Grain 状态、提醒器、流及遥测 | 每个参与的主机均能访问其配置的依赖项 |

HTTP 入口不是 Orleans 的传输机制。Web API 可以与 Silo 共享同一进程，但其 HTTP 端口和负载均衡器独立于 Silo 和网关的 TCP 终结点。

本页专门说明部署配置及运维要求。有关使用这些路径的成员资格、消息传递、目录和激活组件，请参阅[运行时架构](../implementation/runtime-architecture.md)。

<a id="listening-and-advertised-endpoints"></a>

## 监听终结点与通告终结点

**监听终结点**是进程接收连接时使用的本地网络接口和端口。**通告终结点**是存储于成员资格信息中的地址和端口，供其他进程建立连接。

在容器网络、网络地址转换或分配外部可路由端口的托管平台中，两者的值可能不同。绑定到 `0.0.0.0` 或 `::` 会使进程在可用的网络接口上监听，但这并不会提供可供通告的有效地址。

对于 Kubernetes 这类采用直接寻址的容器平台：

- 在所有容器网络接口上监听。
- 通告 Pod IP 地址。
- 确保配置的 Silo 端口和网关端口与容器端口一致。

对于映射私有地址或端口的平台：

- 读取平台提供的可路由地址及映射后的端口。
- 通告这些映射后的值。
- 将监听终结点绑定到进程所在环境中实际存在的网络接口和端口。
- 确认所有对等节点都能连接通告的地址和端口。仅在本地成功绑定并不足够。

切勿通告环回地址、不同对等节点解析结果不一致的主机名、用于 Silo 终结点的负载均衡器虚拟 IP，或实例退出后集群提供程序仍可能保留的临时地址。

有关主机端口映射、覆盖网络、容器桥接网络的限制及终结点诊断，请参阅[跨多台主机在容器中运行 Orleans](containers.md)。

<a id="cluster-identity"></a>

## 集群标识

一个逻辑部署中的所有 Silo 和客户端必须在以下配置上保持一致：

- <xref:Orleans.Configuration.ClusterOptions.ServiceId>：应用程序的稳定标识。Grain 存储提供程序可以利用它区分不同应用程序。
- <xref:Orleans.Configuration.ClusterOptions.ClusterId>：特定部署环境或集群的标识。
- 集群成员资格提供程序，以及该提供程序专用的命名空间、数据库、表或键前缀。

不要让预发布环境使用与生产环境共享的 `ClusterId`。蓝绿部署期间应使用不同的集群 ID，除非两个版本被明确设计为同一个集群内相互兼容的成员。

<a id="choose-a-clustering-provider"></a>

## 选择集群成员资格提供程序

集群成员资格提供程序是负责成员资格及网关发现的协调依赖项。它不是 Grain 激活状态的存储库，也不能替代 Grain 存储提供程序。

选择目标环境中已能够可靠运行和维护的提供程序：

- Azure Table Storage：`Microsoft.Orleans.Clustering.AzureStorage`
- ADO.NET 数据库：`Microsoft.Orleans.Clustering.AdoNet`
- Amazon DynamoDB：`Microsoft.Orleans.Clustering.DynamoDB`
- Redis：`Microsoft.Orleans.Clustering.Redis`
- Apache ZooKeeper：`Microsoft.Orleans.Clustering.ZooKeeper`
- Consul：[`Microsoft.Orleans.Clustering.Consul`](../host/configuration-guide/clustering/consul.md)

有关配置和包链接，请参阅[典型配置](../host/configuration-guide/typical-configurations.md)及 [Orleans 包列表](../resources/nuget-packages.md)。Orleans 并不要求使用 Kubernetes 托管包。若在每个集群仅对应一个 `Deployment` 的简单拓扑中使用该包，它是集群成员资格提供程序的补充，而不是替代品。

评估以下方面：

- 每种故障模式下的可用性和一致性保证。
- 新版本发布或故障恢复期间预期的成员资格读写速率。
- 身份验证、传输加密、网络隔离和最小权限。
- 旧成员资格记录的保留与清理。
- 运维责任归属、备份要求及区域级恢复行为。

<a id="network-policy"></a>

## 网络策略

仅允许必要的通信路径：

- Silo 端口：同一集群中受信任的 Silo。
- 网关端口：受信任的 Orleans 客户端。
- 应用程序入口：应用程序的 HTTP、gRPC 或其他公开协议。
- 提供程序终结点：各已配置的提供程序所需的身份及目标地址。

不要将 Silo 端口或网关端口暴露到公共互联网。如果客户端需要跨越不受信任的网络，请使用 [Orleans TLS](../host/transport-layer-security.md)，并在外围网络边界强制验证工作负载身份。

<a id="validate-connectivity"></a>

## 验证连通性

在引入流量之前：

1. 记录每个实例通告的 Silo 和网关终结点。
1. 从另一个 Silo 测试到每个通告的 Silo 终结点的 TCP 连通性。
1. 从客户端网络测试到网关终结点的 TCP 连通性。
1. 确认集群成员资格信息中仅包含预期的服务 ID、集群 ID 和仍然存活的实例。
1. 在滚动替换期间以及主机重启后重复上述检查。

平台服务或负载均衡器可以用于 HTTP 入口，但 Orleans 成员资格机制仍必须通告能够路由到每个独立 Silo 的终结点。
