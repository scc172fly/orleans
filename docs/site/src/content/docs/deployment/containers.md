---
title: 在跨多个主机的容器中运行 Orleans
description: 为跨主机容器部署配置可路由的 Orleans 终结点。
ms.date: 08/15/2026
ms.topic: how-to
ms.custom: devops
---

<a id="run-orleans-in-containers-across-multiple-hosts"></a>

# 在跨多个主机的容器中运行 Orleans

只有当每个 Silo 都拥有其他所有 Silo 可以直接访问的唯一终结点时，Orleans 集群才能跨多个容器主机运行。外部 Orleans 客户端同样需要能够访问每个通告的网关终结点。容器发现、共享成员资格表和发布的主机端口本身并不能建立这些网络路径。

应尽可能使用私有路由网络、容器覆盖网络或每个工作负载独立的网络接口。不要将 Orleans Silo 或网关端口暴露到公共互联网。

<a id="choose-an-endpoint-model"></a>

## 选择终结点模型

每个 Silo 分别具有监听终结点和通告终结点：

| 设置 | 含义 | 典型容器值 |
| --- | --- | --- |
| <xref:Orleans.Configuration.EndpointOptions.SiloListeningEndpoint> | 在容器内为 Silo 通信开放的地址及端口 | `0.0.0.0:11111` |
| <xref:Orleans.Configuration.EndpointOptions.GatewayListeningEndpoint> | 在容器内为客户端通信开放的地址及端口 | `0.0.0.0:30000` |
| <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress> | 对等节点及客户端连接的每 Silo 地址 | 可路由的私有容器、任务、Pod 或主机地址 |
| <xref:Orleans.Configuration.EndpointOptions.SiloPort> | 存储在成员资格中的 Silo 端口 | 直接路由或发布的 Silo 端口 |
| <xref:Orleans.Configuration.EndpointOptions.GatewayPort> | 返回给客户端的网关端口 | 直接路由或发布的网关端口 |

绑定 `0.0.0.0` 或 `::` 仅会开放本地接口。不能将通配地址作为通告地址，Orleans 也不会根据容器的绑定推断主机发布的端口。

优先采用以下模型之一：

- **每容器直接寻址：** 为每个容器分配可从所有 Silo 及客户端网络路由到达的私有地址。通告该地址，并在容器内外使用相同端口。
- **每 Silo 主机端口映射：** 通告主机的私有地址及一对唯一的已发布 Silo 端口和网关端口。在容器内部绑定相应的目标端口。

不要通告共享负载均衡器、入口虚拟 IP 或可能将连接路由到其他副本的服务名称。Orleans 成员资格标识的是特定 Silo，而不是可以相互替换的后端实例池。

<a id="configure-listening-and-advertised-endpoints"></a>

## 配置监听终结点和通告终结点

由部署平台将通告地址和已发布端口传递给每个副本。以下示例使用固定的容器目标端口及独立提供的通告值：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_endpoint_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_endpoints":::

例如，容器可以监听 `0.0.0.0:11111` 和 `0.0.0.0:30000`，同时通告私有主机映射 `10.0.8.24:21111` 和 `10.0.8.24:23000`。其他所有 Silo 必须能够连接 `10.0.8.24:21111`，每个外部 Orleans 客户端必须能够连接 `10.0.8.24:23000`。

如果地址或发布端口是动态分配的，应在启动 Orleans 之前获取这些值，并使其在该 Silo 成员资格条目的整个生命周期内保持稳定。替换实例可以使用不同的终结点，但必须使用新的 Silo 世代加入集群。

<a id="provide-private-bidirectional-connectivity"></a>

## 提供私有双向连通性

验证完整的网络连通性矩阵：

1. 从每个 Silo 网络连接到每个通告的 Silo 终结点。
1. 从每个客户端网络连接到每个通告的网关终结点。
1. 使用主机端口转换时，既要从远程主机测试已发布的终结点，也要从所属主机或容器进行测试。某些网络地址转换实现不支持所需的回流（hairpin）路径。
1. 在扩容、缩容、主机替换和滚动部署之后重复上述测试。

防火墙规则、安全组、网络策略及服务网格策略必须允许 Silo 之间建立双向长连接 TCP 通信。客户端只需要访问网关，无需访问 Silo 端口。应将两个端口均限制为受信任工作负载可访问；如果网络不构成可信边界，请使用 [Orleans 传输层安全性](../host/transport-layer-security.md)。

<a id="understand-discovery-versus-connectivity"></a>

## 理解发现与连通性的区别

DynamoDB、Azure Table Storage、ADO.NET、Redis、Consul 或 ZooKeeper 等集群成员资格提供程序负责协调成员资格和网关发现。它记录参与方所通告的终结点，但不会代理 Orleans 通信、创建路由或验证这些终结点是否可达。

因此，即使所有 Silo 都能成功读写同一个成员资格表，集群仍可能无法通信。同样，客户端可能成功发现活动网关，却因其网络无法访问一个或多个通告网关终结点而超时。

HTTP 入口和应用服务发现是独立的机制。前端负载均衡器可以将 HTTP 请求路由至应用，但 Orleans 的 Silo 和网关连接仍必须访问从集群成员资格中选出的特定成员。

<a id="account-for-container-platform-behavior"></a>

## 考虑容器平台的行为

- 主机本地的桥接网络地址通常仅在该主机上有意义。除非平台明确支持在主机间路由该地址范围，否则不要跨主机通告此地址。
- 已发布端口仅在主机地址可路由、映射仅对应一个 Silo，且该映射从所有必需的源网络均可访问时才可用。
- 同一主机上的多个 Silo 共享主机地址时，必须具有不同的通告 Silo 端口及网关端口。
- 可以任意选择副本的动态或共享负载均衡映射无法保留 Silo 标识，不适合作为通告终结点。

对于 Docker Engine，用户定义的 [覆盖网络](https://docs.docker.com/engine/network/drivers/overlay/) 可以提供跨主机容器连通性；独立容器仍要求参与主机处于 Swarm 模式。若使用主机桥接网络和已发布端口，应显式通告主机私有地址及每个容器唯一的发布端口。

在 Amazon ECS 中，[`awsvpc` 任务网络](https://docs.aws.amazon.com/AmazonECS/latest/developerguide/task-networking.html) 为每个任务分配自己的网络接口和私有地址，符合直接寻址模型。其他网络模式需要显式验证主机端口的唯一性、路由及安全组规则。DynamoDB 成员资格表可以在 AWS 上发现 Silo，但无法弥补任务或主机终结点不可达的问题。

对于编排平台和托管平台，还请参阅 [平台要求](platform-guides.md)、[在 Kubernetes 上托管 Orleans](kubernetes.md) 及 [在 Azure Container Apps 上托管 Orleans](deploy-to-azure-container-apps.md)。

<a id="complete-platform-qualification"></a>

## 完成平台资格验证

本页确立了容器平台的终结点与路由模型。所选编排器或托管服务还需提供其余生产环境机制：

- 工作负载标识，或受管理的机密及证书交付机制。
- 启动、就绪、存活及依赖项健康检查，以及经过测量的终止期限。
- 资源策略、经过测试的最少 Silo 数量、扩缩容控制及故障域放置。
- 有明确边界的滚动替换，或具备兼容状态处理机制的隔离蓝绿集群。
- 集中的日志、指标、跟踪、部署标识、提供程序遥测及事件处理访问能力。
- 版本化的基础设施和工作负载配置，以及可重复执行的验证。

参阅 [选择部署目标](choose-deployment-target.md) 中持续维护的目标平台指南。对于其他编排器，请在接入生产流量之前完成 [平台要求](platform-guides.md) 和 [生产就绪检查清单](production-readiness.md) 中的验证。

<a id="diagnose-forwarding-loops-and-timeouts"></a>

## 诊断转发循环及超时

首先检查确切的终结点，而不是只检查成员资格提供程序的健康状况：

1. 记录每个进程实际生效的通告 Silo 终结点、通告网关终结点及监听终结点。
1. 检查成员资格记录，确认每条活动记录均包含唯一、符合预期且可路由的 Silo 终结点。
1. 从每个 Silo 容器测试到所有活动的通告 Silo 终结点的 TCP 连通性。从客户端网络测试每个通告网关终结点。
1. 对照容器目标端口、主机或平台映射、防火墙规则以及通告端口。
1. 将故障与实例替换关联分析，并且仅在确认旧 Silo 不可能仍在某个网络分区中运行后，才移除过期成员资格记录。

使用平台提供的 TCP 测试工具。例如：

```bash
nc -vz 10.0.8.24 21111
```

```powershell
Test-NetConnection 10.0.8.24 -Port 21111
```

常见迹象包括：

| 症状 | 可能的终结点问题 |
| --- | --- |
| 成员资格正常，但调用在请求超时时间届满后失效 | 发现成功，但通告的传输终结点被阻止或不可路由。 |
| 日志或成员资格记录显示 `127.0.0.1`、主机本地桥接地址或容器目标端口 | Silo 通告的是绑定侧地址或端口，而非其他参与方可访问的映射。 |
| 多个活动 Silo 通告同一地址和端口 | 共享负载均衡器或不唯一的主机映射无法选中预期的 Silo。 |
| 重复转发、连接尝试或超时消息指向本应代表本地或目标 Silo 的终结点 | 通告标识与该 Silo 的可达终结点不匹配。消息可能被重试或转发，却未能到达预期进程。 |
| TCP 连接成功，但流量抵达其他副本 | 代理、入口或负载均衡器正在将 Orleans 传输连接分发到不同副本。 |

首先纠正终结点映射。延长响应超时时间或更换集群成员资格提供程序，都无法使不可达的通告终结点变得可路由。有关更广泛的事件分诊，请参阅 [部署故障排查](troubleshooting-deployments.md)。
