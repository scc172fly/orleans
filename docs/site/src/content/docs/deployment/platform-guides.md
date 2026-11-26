---
title: "平台要求"
description: "评价主机平台能否安全运行 Orleans 生产集群。"
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="platform-requirements"></a>

# 平台要求

Orleans 可以在 Kubernetes、管理下的集装箱平台、虚拟机器和光金属主机上运行。该平台必须支持 Orleans的流程和网络模型;仅用平台名称并不能使部署生产就绪。

<a id="required-capabilities"></a>

## 所需能力

生产平台必须提供:

- 稳定的每次访问地址或明确公布的终端点绘图。
- 从每个 silo 到每一个 silo 终点直接双向TCP连接。
- 从 Orleans 客户到广告网关端点的可达性。
- 多个同时运行的事例和符合应用程序可用性目标的有文件记录的故障域模型。
- 优异的终止通知和可配置的终止期限。
- 启动,准备和活性检查 用不同的语义。
- 安全地接触支持的集群供应商和所有国家供应商。
- 工作负荷身份或安全的秘密和证书交付。
- 中央日志、度量、跟踪和部署元数据。
- 资源请求、限制、规模控制和干扰政策。

仅使用 HTTP 平台是不够的, 除非 Orleans silos 也能建立直接的 TCP 连接到个别实例。 一个负载平衡的 HTTP 端点不能取代 silo- - 到silo 连接 。

<a id="platform-guidance"></a>

## 平台指导

- [选择部署目标](choose-deployment-target.md) 将维持的地形、终点模型、规模控制、故障域定位、业务所有权和共同供应商选择等进行比较。
- [Azure Kubernetes 服务](azure-kubernetes-service.md) 将 Azure CNI网络、工作量身份、可用区、节点集合、自动升级、升级和 Azure 监测应用于平台中立 Kubernetes 基准。
- [多东道集装箱部署](containers.md) 要求为每个 silo提供直接的每个集装箱私人地址或独特的、可相互接触的东道港口制图。
- [Kubernetes](kubernetes.md) 提供直接的舱位网络。 建议了明确的端点配置; Orleans 主机包是可选的,仅限于简单的一个`Deployment`- 每个组群的表层。
- [服务制造](service-fabric.md) 使用申请方的无国籍可靠服务融合,并配有运行时间分配终端和外部 Orleans 集群提供者。
- [Azure 服务处](azure-app-service.md) 使用 [视窗](deploy-to-azure-app-service.md) 和 [林勒](deploy-to-azure-app-service-linux.md)的私人每份入场地址和港口测绘。
- [Azure 集装箱套件](deploy-to-azure-container-apps.md) 可以在内部环境的私人 IP 上绘制独特的 TCP 端口对到每个 silo 应用程序的一个复制件; 缩放多个 silos , 因为一个应用程序的复制件依赖于每个复制的网络, 而该平台没有作为支持的合同发布。

对于另一个平台, 请证明需要的能力, 至少在滚动替换、 比例化、 主机重新启动和网络中断下有三 silos 。 在部署运行手册中记录准确的端点映射和关闭行为 。

<a id="shared-responsibility"></a>

## 分担责任

Orleans 提供成员资格、启动 grain 、安置和呼叫路由。

- 提供持久性状态和提供者。
- 正确的健康终点和退化模式政策。
- 网络安全和运输加密。
- 兼容释放和州迁移。
- 能力、超载控制、备份、恢复和事件反应。
