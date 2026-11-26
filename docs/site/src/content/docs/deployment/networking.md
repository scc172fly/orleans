---
title: "地形学、联网和集群"
description: "配置 Orleans 终端、网络路径和集束发现,供生产。"
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="topology-networking-and-clustering"></a>

# 地形学、联网和集群

部署 Orleans 有三种不同的网络路径:

| 路径路径路径 | 目的目的宗旨 | 所需可达性 |
| --- | --- | --- |
| Silo 至 silo | 成员查询、 grain 电话、目录流量和运行时间协调 | 每一 silo 向每张广告 silo 终点 |
| 客户端到网关 | Grain 接到的电话 Orleans 客户客户 | 每一个客户 进入广告的网关端点 |
| 提供主机 | 成员、 grain 状态、提醒、流和遥测 | 每个参与的东道主的配置依赖性 |

HTTP 入侵不是一个 Orleans 运输工具。 网络 API 可以与一个 silo共享进程, 但是其 HTTP 端口和负载平衡器与 silo 端点和网关 TCP 端点是分开的。

本页拥有部署配置和操作要求。成员、信息、目录和使用这些路径的激活组件见 [运行时间结构](../implementation/runtime-architecture.md)。

<a id="listening-and-advertised-endpoints"></a>

## 倾听和广告宣传终点

A **收听终端** 是一个进程接受连接的本地界面和端口。 **广告的终点** 即地址和港口储存在成员中,以便其他进程能够连接。

这些值在集装箱网络、网络地址翻译或指定外部路由端口的主机平台之间可能有所不同。 绑定到 `0.0.0.0` 或 `::` , 使程序能监听可用的接口; 它不提供可用于广告的地址 。

直接地址集装箱平台,如 Kubernetes:

- 听所有集装箱接口。
- 广告发布舱IP。
- 保持配置 silo 和网关端口与集装箱端口相等。

用于绘制私人地址或港口地图的平台:

- 阅读平台提供的路线地址并绘制港口地图。
- 公布这些地图上的值。
- 将监听端点绑在程序内存在的界面和港口上。
- 确定每个同行都能连接到广告中的价值。 成功的地方约束是不够的 。

永远不要广告回溯回转,一个对等方不同解析的主机名、一个 silo 端点的负载平衡虚拟IP或集群提供者在事件消失后可以保留的一个时间段地址。

东道港口测绘、重叠网络、集装箱桥限制和终点诊断见 [运行 Orleans 多个主机的集装箱](containers.md)。

<a id="cluster-identity"></a>

## 群群身份

所有 silos 和客户在一个逻辑部署中必须商定:

- <xref:Orleans.Configuration.ClusterOptions.ServiceId>: The stable identity of the application. Grain storage providers can use it to separate applications.
- <xref:Orleans.Configuration.ClusterOptions.ClusterId>: The identity of a particular deployment environment or cluster.
- 集群提供商及其特定提供商的名称空间、数据库、表格或关键前缀。

不要使用共享产品 `ClusterId` 在部署期间,使用不同的集束识别码,除非两种版本都是有意兼容的一组成员。

<a id="choose-a-clustering-provider"></a>

## 选择群集提供商

集群提供商是一个协调依赖者,用于加入和网关发现。它不是 grain 启动状态的存储处,并且不取代 grain 储存提供者。

选择一个已经在目标环境中可靠运行的提供者 :

- Azure 表格储存: `Microsoft.Orleans.Clustering.AzureStorage`
- ADO.NET 数据库: `Microsoft.Orleans.Clustering.AdoNet`
- 亚马逊 DynamoDB: `Microsoft.Orleans.Clustering.DynamoDB`
- Redis: `Microsoft.Orleans.Clustering.Redis`
- 阿帕奇 ZooKeeper: `Microsoft.Orleans.Clustering.ZooKeeper`
- 领事: [`Microsoft.Orleans.Clustering.Consul`](../host/configuration-guide/clustering/consul.md)

配置和包件链接见 [典型配置](../host/configuration-guide/typical-configurations.md) 和 [Orleans 套件列表](../resources/nuget-packages.md) 。 Orleans 不需要 Kubernetes 主机包。如果用于简单的一个`Deployment`-每个组的地形,它补充了一个集群提供者;它不替换一个。

评价:

- 在每个故障模式中,都有可用性和一致性保障。
- 在推出或恢复期间,预期会员读写/写费率。
- 验证、交通加密、网络隔离和最不特权
- 保留和清理旧会员行。
- 行动所有权,备份要求,以及区域恢复行为。

<a id="network-policy"></a>

## 网络政策

只允许所需的路径 :

- Silo 端口:在同一组中信任 silos 。
- 网关端口: 信任 Orleans 客户端。
- 反向应用:应用程序的 HTTP gRPC或其他公共协议。
- 提供方端点:每个配置的提供者所需的身份和目的地。

不要将 silo 港口或网关的端口暴露在公共互联网上。如果客户跨越一个不信任的网络,请使用 [Orleans TLS 电信](../host/transport-layer-security.md) 并在周围网络边界处强制确定工作量。

<a id="validate-connectivity"></a>

## 验证连接

在发送通讯前:

1. 记录每个实例的广告 silo 和网关端点。
1. 测试 TCP 从另一个 silo 连接到每个广告的 silo 终点。
1. 测试TCP从客户网络连接到网关端点的连接。
1. 确认分组成员仅包含预期的服务编号、分组身份和现场实例。
1. 在滚动替换和主机重新启动后重复检查。

平台服务或负载平衡器对HTTP的入侵可能有用,但 Orleans 成员仍须公布通往每个个人 silo的终点。
