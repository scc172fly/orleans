---
title: 在 Azure Kubernetes Service 上托管 Orleans
description: 在 AKS 上部署和运维 Orleans 集群，使用 Pod 直接联网、工作负载标识、可用区感知放置、自动缩放和受控升级。
ms.date: 08/21/2026
ms.topic: how-to
ms.custom: devops
---

<a id="host-orleans-on-azure-kubernetes-service"></a>

# 在 Azure Kubernetes Service 上托管 Orleans

Azure Kubernetes Service (AKS) 提供托管的 Kubernetes 控制平面运维，而 Orleans 作为应用程序工作负载运行。每个 Pod 运行一个 Silo，通告 Kubernetes Downward API 提供的 Pod IP，并使用外部生产级集群提供程序。

首先参阅与平台无关的 [Kubernetes 指南](kubernetes.md)，了解 Orleans `Deployment`、终结点配置、探针、资源、中断预算和优雅关闭的基本配置。本指南在此工作负载的基础上，介绍 Azure 网络、标识、可用性、缩放、升级和可观测性。

<a id="choose-the-aks-operating-model"></a>

## 选择 AKS 运维模式

[AKS Automatic](https://learn.microsoft.com/azure/aks/intro-aks-automatic) 为网络、节点池、安全性和升级提供托管的生产级默认配置。[AKS Standard](https://learn.microsoft.com/azure/aks/what-is-aks) 则允许直接控制网络地址管理、节点池、升级策略及集群功能。

如果外部 Orleans 客户端需要直接路由到网关 Pod IP，或者工作负载需要自定义节点池、网络或升级控制，请选择 AKS Standard。记录选定的集群模式，以及仍由应用程序负责的 Orleans 专属设置：

- 生产级集群、Grain 存储、提醒器、流和 Grain 目录提供程序。
- Pod 终结点配置、集群标识、健康检查终结点和关闭行为。
- 资源请求、缩放阈值、中断策略和最少 Silo 数量。
- 应用程序兼容性、部署顺序、回滚和灾难恢复。

<a id="choose-an-aks-network-model"></a>

## 选择 AKS 网络模型

Orleans Silo 需要 Pod 之间的直接 TCP 连通性。[由 Cilium 提供支持的 Azure CNI](https://learn.microsoft.com/azure/aks/azure-cni-powered-by-cilium) 支持此通信路径，并强制实施 Kubernetes 网络策略。

根据 Orleans 客户端拓扑选择 IP 地址管理模型：

| AKS 网络模型 | Silo 间通信路径 | Orleans 客户端部署位置 |
| --- | --- | --- |
| [Azure CNI Overlay](https://learn.microsoft.com/azure/aks/concepts-network-azure-cni-overlay) | Pod 使用 Pod IP 通过集群覆盖网络直接通信。 | 在 AKS 集群内部运行 Orleans 客户端。集群外部终结点访问应用程序入口或 Kubernetes 服务，而不是覆盖网络中的 Pod IP。 |
| [Azure CNI Pod Subnet](https://learn.microsoft.com/azure/aks/concepts-network-azure-cni-pod-subnet) | Pod 从委派的 Pod 子网获取虚拟网络地址。 | 当对等虚拟网络或已连接的本地网络中的受信任客户端需要直接路由到每个已通告的网关 Pod IP 时使用。 |

严格按照[在 Kubernetes 上托管 Orleans](kubernetes.md#configure-the-silo) 所示，使用 `POD_NAME` 和 `POD_IP` 配置 Silo。通告 Pod IP，在所有 Pod 网络接口上监听，并确保各 Pod 使用一致的 Silo 端口和网关端口。

Kubernetes `Service`、入口控制器或[适用于容器的应用程序网关](https://learn.microsoft.com/azure/application-gateway/for-containers/overview) 可以公开 HTTP 或 gRPC 应用程序流量。Orleans 客户端仍通过成员资格信息发现并直接连接各个网关 Pod IP。

<a id="create-the-private-network-boundary"></a>

## 创建专用网络边界

[专用 AKS 集群](https://learn.microsoft.com/azure/aks/private-clusters) 为 Kubernetes API 服务器提供专用终结点。将专用控制平面与以下配置结合使用：

- 按常规扩缩容、升级期间额外容量和故障恢复需求确定大小的专用节点子网及 Pod 子网。
- 互不重叠的 Pod、服务、节点、对等网络、VPN 和 ExpressRoute 地址空间。
- 用于集群、状态、提醒器、流、注册表、遥测和机密等依赖项的专用终结点或经批准的出站路径。
- 允许 Silo 间 TCP、客户端到网关的 TCP、DNS、健康探针以及确切的提供程序出站目标的网络策略。
- API 服务器和各个专用 Azure 依赖项的专用 DNS 解析。

将 Silo 端口限制为仅 Orleans 工作负载可访问，将网关端口限制为仅受信任的 Orleans 客户端可访问。当网络边界要求工作负载级别的身份验证和加密时，使用 [Orleans 传输层安全性](../host/transport-layer-security.md)。

从每个工作负载标识及网络验证实际生效的策略。网络策略可能允许 Pod 流量，而子网安全规则、防火墙、专用终结点策略或路由表却阻止同一通信路径。

<a id="configure-workload-identity-and-dependencies"></a>

## 配置工作负载标识和依赖项

[Microsoft Entra Workload ID](https://learn.microsoft.com/azure/aks/workload-identity-overview) 将 Kubernetes 服务帐户与 Microsoft Entra 标识建立联合关系。AKS Automatic 预先配置集群 OIDC 颁发者和工作负载标识功能。在 AKS Standard 上启用这两项功能，然后为每种工作负载角色分配专用服务帐户。

当 Silo、客户端和运维作业的权限不同时，为它们使用不同的运行时标识。为每个标识授予以下资源所需的最小范围数据平面角色：

- 集群成员资格存储。
- 命名 Grain 存储提供程序。
- 提醒器和流提供程序。
- Key Vault 机密和证书。
- 应用程序直接向目标进行身份验证时所需的遥测导出权限。

Azure Table Storage 是常见的集群提供程序。ADO.NET 或 Redis 也可以纳入现有的托管依赖项策略。参照[拓扑、网络与集群](networking.md#choose-a-clustering-provider) 选择提供程序，并分别配置持久化状态、提醒器和流。健康的成员资格表能够支持集群发现，但每个应用程序数据依赖项仍有各自的持久性和恢复要求。

使用 Key Vault 引用、Secrets Store CSI Driver 或工作负载标识直接访问的方式获取机密和证书。设置轮换和过期警报，并验证轮换后的值能否按照应用程序生命周期传递给新建及现有 Pod。

AKS 会在 Pod 工作负载标识可用之前拉取容器映像。对于 Azure Container Registry，为 AKS kubelet 标识授予 `AcrPull`；对于使用存储库范围 ABAC 权限的注册表，则授予 `Container Registry Repository Reader`。请参阅[将 Azure Container Registry 与 AKS 集成](https://learn.microsoft.com/azure/aks/cluster-container-registry-integration)。

<a id="distribute-silos-across-failure-domains"></a>

## 将 Silo 分布在不同故障域中

如果 Orleans 工作负载的隔离、虚拟机规格、缩放或维护策略与系统组件不同，请为其创建专用用户节点池。AKS 保留一个 Linux 系统节点池以运行关键集群服务；应用程序用户节点池可以使用独立的标签、污点和自动缩放范围。请参阅[在 AKS 中创建节点池](https://learn.microsoft.com/azure/aks/create-node-pools)。

在[支持 AKS 可用区](https://learn.microsoft.com/azure/aks/reliability-availability-zones-configure) 的区域中：

1. 创建跨可用区或与可用区对齐的用户节点池，使用所有选定可用区均可用的虚拟机规格。
1. 当经过验证的可用性目标要求每个可用区有一个就绪 Silo 时，运行至少三个 Silo 副本。
1. 添加 `topologySpreadConstraints`，使用 `topology.kubernetes.io/zone` 和 `kubernetes.io/hostname`。
1. 添加 Pod 反亲和性，使调度器将 Silo 分散到不同节点。
1. 保留足够的配额、子网地址和额外容量，以便在一个可用区或节点池不可用时替换节点。

当存在符合条件的容量时，Kubernetes 调度约束会实现所要求的分布。部署后及节点池升级期间，验证 Pod 在各可用区的实际分布。

跨可用区的提供程序是此设计的另一必要组成部分。为集群、状态、提醒器、流、注册表、Key Vault 和遥测服务配置相同的可用性目标，并测试某个依赖项的一个可用区不可用时应用程序的行为。

<a id="configure-health-shutdown-and-disruption-policy"></a>

## 配置健康检查、关闭和中断策略

采用[在 Kubernetes 上托管 Orleans](kubernetes.md#health-probes) 中的启动、就绪及存活行为：

- 配置有效、监听器已绑定、所需初始化完成且 Silo 加入目标集群后，启动检查成功。
- 在关闭之前，以及应用程序无法安全接受新工作时，就绪状态变为 false。
- 存活检查报告本地进程的运行进展，并且不依赖共享的远程依赖项。

将 `terminationGracePeriodSeconds` 设置为大于实测的 .NET 主机关闭超时。这样，AKS 排空节点和替换工作负载时，主机就有时间报告未就绪状态、停止应用程序工作，并让 Orleans 离开集群成员资格。

`PodDisruptionBudget` 限制同时发生的自愿驱逐次数。当有足够的可调度容量时，它在节点排空和升级期间维持就绪 Silo 数量下限。节点故障和可用区丢失仍属于突发故障，因此应根据这些事件发生后仍能承担负载的需求确定集群规模。

对于兼容版本，使用 `maxUnavailable: 0`、受限的额外副本数和 `minReadySeconds` 执行滚动更新。使探针时间参数、进度截止时间、PDB、节点排空超时及主机关闭超时相互协调，以便每次发布要么在有足够容量时继续推进，要么以可观察的方式暂停。

<a id="scale-pods-and-nodes"></a>

## 缩放 Pod 和节点

Orleans 放置机制使用当前在成员资格中处于活动状态的 Silo。应用程序或平台自动缩放器改变 Silo Pod 数量；当 Pod 无法调度或可以安全排空节点时，[AKS 集群自动缩放器](https://learn.microsoft.com/azure/aks/cluster-autoscaler) 添加或移除节点。

协同配置这两个层级：

- 将最少 Silo 副本数保持在经过测试的容量及冗余下限。
- 根据持续 CPU 使用率、队列深度、请求延迟、拒绝率或有界自定义指标等应用程序信号缩放 Silo Pod。
- 使资源请求能够反映实际需求，以便待调度 Pod 准确表示节点容量需求。
- 为节点池自动缩放器提供足够的最大容量、配额和子网地址空间，以支持恢复和发布期间的额外容量。
- 使用稳定窗口和渐进式缩容，使成员资格变化、激活迁移和提供程序负载能在每次移除之间趋于稳定。
- 保持系统节点池与 Orleans 用户节点池策略相互独立。

节点自动缩放器管理 AKS 虚拟机规模集的容量。应保留它对缩放设置的控制权，不要另外为虚拟机规模集配置独立的自动缩放。

通过负载测试模拟需要增加 Pod 和节点的突发负载、丢失一个 Silo 和一个节点、受控缩容以及提供程序响应变慢。确认应用程序在整个过程中都符合延迟和拒绝率目标。

<a id="plan-application-and-aks-upgrades"></a>

## 规划应用程序与 AKS 升级

应用程序滚动升级会让新旧 Silo 暂时共存于一个集群中。保持 Grain 接口、序列化器、持久化状态、提醒器、流、提供程序架构和外部副作用的兼容性。对于不兼容的版本，使用独立的 `ClusterId` 并迁移流量。

AKS 还会升级 Kubernetes 控制平面和节点池。使用 [AKS 升级选项](https://learn.microsoft.com/azure/aks/upgrade-options) 及计划维护来执行以下操作：

1. 在具有代表性的预生产集群中验证 Kubernetes API 兼容性及 Orleans 工作负载。
1. 保留配额、子网地址和节点池升级所需的额外容量。
1. 排空有限数量的节点，同时通过 PDB 和发布策略保留就绪 Silo。
1. 监控成员资格、重新激活负载、提供程序延迟、请求结果和 Pod 放置情况。
1. 超过容量或应用程序阈值时暂停升级。

在应用程序新旧版本混合运行且存在新版本写入的状态时测试回滚。对于无法就地回滚的集群级变更，预配第二个 AKS 集群，使用不同的 Orleans `ClusterId`，验证后按照[蓝绿升级指南](upgrades.md#blue-green-upgrades) 迁移应用程序流量。

<a id="configure-observability-and-incident-access"></a>

## 配置可观测性和事件处置访问权限

将 Orleans 日志、.NET 指标、分布式跟踪、Kubernetes 事件、容器日志及 AKS 控制平面诊断信息导出到集中式系统。[Azure Monitor 的托管 Prometheus 服务](https://learn.microsoft.com/azure/azure-monitor/metrics/prometheus-metrics-overview) 和 [Azure Managed Grafana](https://learn.microsoft.com/azure/managed-grafana/overview) 可以提供 Azure 托管的指标监控路径。

关联以下信息：

- AKS 集群、命名空间、节点池、节点、可用区、Pod、Deployment 和 ReplicaSet。
- Orleans 服务 ID、集群 ID、Silo 名称、已通告的终结点及应用程序版本。
- 容器映像摘要、部署操作、提供程序请求及工作负载标识。

针对持续的用户影响、就绪 Silo 数量下降、发布失败或阻塞、待调度 Pod、节点压力、循环重启、成员资格频繁变更、提供程序限流、凭据过期以及容量耗尽设置警报。

确保在专用集群边界内仍可访问系统进行事件处置。运维人员需要经过测试的访问路径，以便在部分服务中断时运行 `kubectl`、检查成员资格、测试每个已通告的终结点、查询提供程序并获取之前的容器日志。请参阅[排查部署故障](troubleshooting-deployments.md#kubernetes-checks)。

<a id="validate-the-production-deployment"></a>

## 验证生产部署

在引入生产流量之前，以及网络、标识、节点池或升级策略发生实质性变更之后，完成以下检查：

1. 确认每个活动 Silo 的成员资格行都包含预期的 Pod IP，以及唯一的 Silo 和网关终结点对应的端口。
1. 测试每个 Silo Pod 到所有已通告 Silo 终结点的 TCP 连通性。
1. 从每个 Orleans 客户端网络测试所有已通告的网关终结点。
1. 使用分配给 Silo 和客户端的工作负载标识，验证网络策略的实施情况以及提供程序访问权限。
1. 确认 Pod 在各节点和可用区的实际分布。
1. 在有负载时替换一个 Silo Pod，验证其优雅离开集群成员资格以及激活恢复。
1. 排空一个节点，验证中断预算、终止截止时间、替换调度及容量下限。
1. 演练 Pod 扩容、节点扩容、渐进式缩容，以及丢失一个可用区或节点池的容量。
1. 执行一次兼容的滚动部署和回滚。
1. 在隔离的集群中恢复持久化应用程序数据，并验证恢复操作手册。

记录实际生效的清单、集群网络模型、服务和 Pod CIDR、节点池配置、标识、角色分配、提供程序命名空间、探针阈值、关闭截止时间、缩放范围和运维命令。完成通用的[生产就绪检查清单](production-readiness.md)。
