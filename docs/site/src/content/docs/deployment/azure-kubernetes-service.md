---
title: "东道主主 Orleans 日期 时 Azure Kubernetes 服务处"
description: "部署并运行一个 Orleans AKS组,直接建立舱面网络、工作量识别、区意识定位、自动升级和控制升级。"
ms.date: 08/21/2026
ms.topic: how-to
ms.custom: devops
---

<a id="host-orleans-on-azure-kubernetes-service"></a>

# 东道主主 Orleans 日期 时 Azure Kubernetes 服务处

Azure Kubernetes 服务(AKS)提供管理 Kubernetes 控制飞机操作期间 Orleans 运行为应用程序工作量。使用 silo 广告宣传由 Kubernetes 下下向 API,并使用外部生产集群提供商。

以平台中中性开始 [Kubernetes 指南](kubernetes.md) 排排 Orleans `Deployment`,端点配置、探测器、资源、中断预算以及优雅的关闭基线。本指南适用于 Azure 网络建设、身份、可用性、规模、升级和遵守这一工作量。

<a id="choose-the-aks-operating-model"></a>

## 选择 AKS AKS 操作模式

[AKS 自动自动](https://learn.microsoft.com/azure/aks/intro-aks-automatic) 提供网络化、节点集合、安保和升级方面的管理下生产缺省。 [AKS AKS标准](https://learn.microsoft.com/azure/aks/what-is-aks) 直接控制网络地址管理、节点集合、升级政策和集群特征。

当外部 Orleans 客户需要直接连接缓存IP的通道时,或者当工作量需要自定义节点库、网络或升级控制时,选择 AKS 标准。 请记录选定的集束模式和 Orleans- 特定设置,这些设置仍为应用程序所有:

- 生产集群、 grain 储存、提醒、流和 grain-- -- 指令提供者。
- 末点配置、集群身份、健康终点和停机行为
- 资源请求、标定阈值、干扰政策和最低 silo 计数。
- 应用兼容性、部署顺序、回落和灾后恢复。

<a id="choose-an-aks-network-model"></a>

## 选择 AKSS 网络模式

Orleans silos 需要直接的舱到船的TCP连通性。 [Azure 丙烯基离子](https://learn.microsoft.com/azure/aks/azure-cni-powered-by-cilium) 支持此路径和强制执行 Kubernetes 网络政策。

从 Orleans 客户端表 选择 IP 地址管理模式:

| AKSAAKS网络模式 | Silo- 至-silo 路径 | Orleans 客户安置 |
| --- | --- | --- |
| [Azure CNI 重叠](https://learn.microsoft.com/azure/aks/concepts-network-azure-cni-overlay) | 使用舱载IP直接在集群覆盖上进行沟通。 | AKS 集群内的客户运行 Orleans 。 集群外的端点会达到应用程序入侵或 Kubernetes 服务, 而不是覆盖缓存IP 。 |
| [Azure CNNI 水底网](https://learn.microsoft.com/azure/aks/concepts-network-azure-cni-pod-subnet) | 民意调查从授权的pod子网获得虚拟网络地址。 | 当同行虚拟网络或连接的房地网络中的受信任客户需要直接通往每一个广告网关IPIP的线路时,使用。 |

配置 silo 调自 `POD_NAME` 和 `POD_IP` 精确显示于 [东道主主 Orleans 日期 时 Kubernetes](kubernetes.md#configure-the-silo)。在广告中发布 IP , 在所有 IP 界面上收听 silo 和网关港口稳定 跨越舱。

Kubernetes `Service`、入侵控制器或 [集装箱应用网关](https://learn.microsoft.com/azure/application-gateway/for-containers/overview) 可暴露 HTTP 或 gRPC 应用程序的流量。 Orleans 客户继续发现和拨打存储在其成员中的单个网关IP。

<a id="create-the-private-network-boundary"></a>

## 创建私有网络边界

[私营AKS 集束式](https://learn.microsoft.com/azure/aks/private-clusters) 给 Kubernetes API 服务器一个私有端点。 将私人控制平面与以下组合:

- 私人节点和播客子网,其规模为普通规模、升级激增和故障恢复。
- 空格、非重叠的播客、服务、节点、对等网络、VPN和 ExpressRoute 地址空间。
- 私营端点或核准的集群、状态、提醒、流、登记、遥测和秘密依赖的转移路径。
- 允许 silo-至silo TCP、客户端到开口TCP、DNS、健康探测和确切提供者离开目的地的网络政策。
- 用于 API 服务器和每个私有 Azure 依赖的私钥 DNS 分辨率。

将 silo 端口限制在 Orleans 工作量和网关端口,使信任的客户能够使用 Orleans 。当网络边界需要工作量水平的认证和加密时,请使用 [Orleans 运输层安全](../host/transport-layer-security.md) 。

验证每个工作量身份和网络的有效政策。 网络政策可以允许舱面交通,而亚网安全规则、防火墙、私人端点政策或路线表则堵塞了相同的路径。

<a id="configure-workload-identity-and-dependencies"></a>

## 配置工作量身份和依赖关系

[Microsoft 连载工作量识别码](https://learn.microsoft.com/azure/aks/workload-identity-overview) 将一个 Kubernetes 服务账户与 Microsoft Entra身份连接。 AKS自动预设了OIDC集群发行人和工作量识别能力。启用AKS标准的两个功能,然后为每个工作量角色指定一个专用服务账户。

在允许不同的情况下, silos、客户和操作工作使用单独的运行时间身份。

- 集群会员商店。
- 命名为 grain 存储供应商。
- 提醒和流源提供者。
- 密钥地下秘密和证书
- 当应用程序认证直接到目的地时, 遥测导出 。

Azure 表格存储是一个常见的集群提供者。 ADO.NET 或 Redis 也可以适应现有的管理下的抚养战略。选择使用 [地形学、联网和集群](networking.md#choose-a-clustering-provider)的供应商,并独立配置耐久状态、提醒和流。健康的会员名单可以确定发现;每个应用程序数据依赖性都保留自己的持久性和恢复要求。

使用密钥跳板引用、 秘密存储 CSI 驱动程序, 或者直接获取机密和证书的工作- 工作量识别权限。 设置旋转和过期警告并核实一个旋转值是否根据应用生命周期到达新的和现有的缓存舱。

AKS在舱载工作量身份确定之前拉动容器图像。对于 Azure 集装箱登记,应允许使用存放处范围ABAC许可的登记册使用AKS kubelet 身份 `AcrPull`或 `Container Registry Repository Reader` 。见 [将 Azure 集装箱登记簿与AKS合并](https://learn.microsoft.com/azure/aks/cluster-container-registry-integration)。

<a id="distribute-silos-across-failure-domains"></a>

## 将 silos 分配到失败域

当孤立、 VM 缩放、缩放或维护政策与系统组件不同时,AKS为 Orleans 工作量创建一个专门的用户节点集合。 AKS为关键集束服务保留了一个Linux系统节点集合;应用程序用户集合可以使用单独的标签、污点和自动缩放界限。 见 [在 AKS 中创建节点集合](https://learn.microsoft.com/azure/aks/create-node-pools)。

在 [AKS AKS 供应区支助](https://learn.microsoft.com/azure/aks/reliability-availability-zones-configure)区域:

1. 创建跨越或与区域保持距离的用户节点集合,每个选定区都有 VM 大小。
1. 至少跑三步 silo 当测试的可用性目标需要一个备就时复制 silo 每个区。
1. 添加添加添加 `topologySpreadConstraints` 使用 `topology.kubernetes.io/zone` 和 `kubernetes.io/hostname`.
1. 添加缓冲反亲和, 使调度器通过节点分配 silos 。
1. 保持足够的配额、子网地址和快速增援能力,以取代节点,而一个区或节点库则无法使用。

Kubernetes 时间安排方面的制约因素确定了在具备合格能力时所要求的分配。 核查部署后和节点池升级期间实际的对区位置。

跨区提供商完成设计。 配置组合、 状态、 提醒、 流、 注册、 密钥天体和遥测服务, 以相同可用目标为目的, 并在一个依赖区无法使用时测试应用程序的行为 。

<a id="configure-health-shutdown-and-disruption-policy"></a>

## 配置健康、关闭和干扰政策

使用 [东道主主 Orleans 日期 时 Kubernetes](kubernetes.md#health-probes)中的启动、准备和活性行为:

- 配置有效、听众受约束、所需初始化完成、 silo 加入预定组群后启动成功。
- 关闭前以及申请无法安全接受新工作时,准备就绪情况都是假的。
- 生命报告当地进程的进展,并且仍然独立于共同的偏远依赖性。

设定 `terminationGracePeriodSeconds` 时间超过所测量的 .NET 主机关闭超时。 AKS 节点排出和工作量替换后,主机有时间报告未准备情况、停止申请工作并让 Orleans 退出成员身份。

A `PodDisruptionBudget` 保留了一种随时可用的silo 节点故障和区损失仍然是突发的断路,因此在事件发生后需要为集束提供大量服务。

使用与 `maxUnavailable: 0`、 捆绑式的激增和 `minReadySeconds` 相兼容版本的滚动更新。 进行时间、 进度最后期限、 PDB、 节点排出超时和主机关闭超时, 彼此一致, 所以每次推出时要么以能力推进, 要么明显暂停 。

<a id="scale-pods-and-nodes"></a>

## 缩放缓冲和节点

Orleans 位置设置使用当前活跃的 silos 成员。应用程序或平台自动标定器会改变 silo 舱位的数量,当舱位变得无法排成或节点可以安全地排出时, [AKSAKS 集束自动自动标](https://learn.microsoft.com/azure/aks/cluster-autoscaler) 会增加或删除节点。

将两层配置在一起:

- 在测试容量和冗余层保持最小 silo 复制数。
- 应用信号的缩放 silo 舱,例如持续CPU、排队深度、要求延时率、拒绝率或捆绑定制度量。
- 保持资源请求的代表性,以便等待的舱位准确信号节点需求。
- 给节点集合自动标尺器足够的最大容量、配额和子网地址空间,以便进行恢复和推出。
- 使用稳定窗口和逐步加入规模,从而成为成员、启动运动和提供者的负载在清除之间解决。
- 保持系统和 Orleans 用户节点政策独立。

节点自动标尺器管理 AKS 虚拟机级定级能力。 保存对天平设置的拥有权, 而不是应用独立的虚拟机级定级自动缩放 。

装入测试一个增加缓冲和节点的爆破、丢失一个 silo 和一个节点、控制比例尺和供应商减速。 确认该应用在序列中始终处于潜伏和拒绝目标范围之内。

<a id="plan-application-and-aks-upgrades"></a>

## 计划应用程序和AKS升级

应用程序滚动升级将新旧 silos 临时混合在一个组中。保持 grain 接口、序列器、持久性状态、提醒、流、供应商的图案和外部副作用的兼容性。使用单独的 `ClusterId` 和交通迁移来不兼容解密。

AKSA还更新了 Kubernetes 控制平面和节点集合。使用 [AKSS升级选项](https://learn.microsoft.com/azure/aks/upgrade-options) 并计划进行以下维修:

1. 核实具有代表性的预生产组群中的 Kubernetes API 兼容性和 Orleans 工作量。
1. 保留配额、子网地址和节点资源激增能力。
1. 将若干条带界限的节点排入其中,而《公发数据库》和推出政策则保持待命状态 silos。
1. 监测会员、重新启动负荷、供应商的延迟期、请求结果和舱位安排。
1. 超过能力或应用阈值时暂停。

测试回滚,同时存在混合应用程序版本和新版本的状态。对于无法倒回的组群级变化,请提供第二个AKS组,其中有一个单独的 Orleans `ClusterId`,验证它,并利用 [蓝绿色指导指南](upgrades.md#blue-green-upgrades)将应用流量迁移到别处。

<a id="configure-observability-and-incident-access"></a>

## 配置可观察性和事故访问

出口出口出口 Orleans 原木, .NET 测量,分布的痕迹, Kubernetes 活动、集装箱日志和AKS控制飞机对中央系统的诊断。 [Azure 普罗米修斯监控管理服务](https://learn.microsoft.com/azure/azure-monitor/metrics/prometheus-metrics-overview) 和 [Azure 管理下的格拉法纳](https://learn.microsoft.com/azure/managed-grafana/overview) 能够提供 Azure- 管理公制路径

相关日期 :

- AKS 集束、 命名空间、 节点池、 节点、 区、 舱、 部署和复制组 。
- Orleans 服务编号、集束识别码、 silo 名称、广告的终点和应用程序版本。
- 集装箱图像摘要、部署作业、供应商请求和工作量身份。

持续用户影响警报,减少即时-silo 计数、失败或封锁的推出、待决播客、节点压力、重新启动循环、成员数目、供应商抽打、证明过期和耗竭能力。

保持通过私人集束边界随时提供事故进入通道。 `kubectl`检查会员资格,测试每个广告的终点、查询提供者,并在部分停产时检索以前的集装箱日志。 [故障解决部署](troubleshooting-deployments.md#kubernetes-checks).

<a id="validate-the-production-deployment"></a>

## 核实生产部署情况

在生产流通之前和材料网络、身份、节点或升级政策变化之后完成这些检查:

1. 确认每个活跃的 silo 成员行都包含预期的舱位IP和独特 silo 和网关港口。
1. 测试 TCP 从每个 silo 舱到每一个广告的 silo 终点连接。
1. 测试每个客户网络 Orleans 的每一个公布网关端点。
1. 利用分配给 silos 和客户的工作量身份,核查网络政策执法和供应商准入情况。
1. 确认实际的舱位分布 跨越节点和区。
1. 替换一个 silo 舱,装载并核实优雅的会员离开和激活恢复。
1. 将一个节点排入干燥处,并核实干扰预算、终止期限、更换时间表和能力底线。
1. 行动舱规模扩大、节点规模扩大、逐步规模扩大和单区或一中点能力丧失。
1. 执行兼容的滚动部署和回击
1. 在一个孤立的集组中恢复耐用应用数据,并验证回收运行手册。

记录有效清单、集群网络模型、服务和舱载光数据数据库、节点组合、身份、角色分配、供应商命名空间、探测阈值、关闭最后期限、缩放界限以及操作命令。完成共享 [生产准备状态清单](production-readiness.md)。
