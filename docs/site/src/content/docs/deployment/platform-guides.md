---
title: 平台要求
description: 评估托管平台能否安全运行 Orleans 生产集群。
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="platform-requirements"></a>

# 平台要求

Orleans 可以运行在 Kubernetes、托管容器平台、虚拟机及裸机主机上。平台必须支持 Orleans 的进程与网络模型；仅凭平台名称无法判定部署是否已具备生产就绪条件。

<a id="required-capabilities"></a>

## 必需能力

生产环境平台必须提供：

- 稳定的每实例地址，或明确的通告终结点映射。
- 每个 Silo 到所有 Silo 终结点的直接双向 TCP 连通性。
- Orleans 客户端到通告网关终结点的可达性。
- 多个并行运行的实例，以及有明确文档说明、满足应用可用性目标的故障域模型。
- 优雅终止通知和可配置的终止期限。
- 语义各自独立的启动、就绪及存活检查。
- 对受支持的集群成员资格提供程序及所有状态提供程序的安全访问。
- 工作负载标识，或安全的机密与证书交付机制。
- 集中的日志、指标、跟踪及部署元数据。
- 资源请求量、限制、扩缩容控制和中断策略。

仅支持 HTTP 的平台并不足够，除非 Orleans Silo 也能直接与各个实例建立 TCP 连接。经过负载均衡的 HTTP 终结点不能替代 Silo 之间的连通性。

<a id="platform-guidance"></a>

## 平台指南

- [选择部署目标](choose-deployment-target.md) 比较已有维护支持的拓扑、终结点模型、扩缩容控制、故障域放置、运维责任和常见的提供程序选项。
- [Azure Kubernetes Service](azure-kubernetes-service.md) 在平台无关的 Kubernetes 基础方案之上，采用 Azure CNI 网络、工作负载标识、可用区、节点池、自动扩缩容、升级以及 Azure Monitor。
- [跨多个主机的容器部署](containers.md) 要求为每个 Silo 提供直接可达的容器专用私有地址，或唯一且对其他参与方可达的主机端口映射。
- [Kubernetes](kubernetes.md) 提供 Pod 之间的直接网络连通性。建议显式配置终结点；Orleans 托管包是可选的，且仅适用于每个集群对应一个 `Deployment` 的简单拓扑。
- [Service Fabric](service-fabric.md) 使用由应用编写的无状态 Reliable Service 集成方案，其中终结点由运行时分配，并使用外部 Orleans 集群成员资格提供程序。
- [Azure App Service](azure-app-service.md) 在 [Windows](deploy-to-azure-app-service.md) 和 [Linux](deploy-to-azure-app-service-linux.md) 上采用每实例私有地址及端口映射。
- [Azure Container Apps](deploy-to-azure-container-apps.md) 可以将内部环境私有 IP 上的唯一 TCP 端口对映射至每个 Silo 应用的一个副本；若通过同一应用的多个副本来扩展多个 Silo，则依赖每副本网络能力，而平台并未将该能力作为受支持的契约发布。

对于其他平台，使用至少三个 Silo，在滚动替换、缩容、主机重启和网络中断的情况下验证必需能力。将确切的终结点映射和关闭行为记录到部署运维手册中。

<a id="shared-responsibility"></a>

## 共同责任

Orleans 提供成员资格管理、Grain 激活、放置及调用路由。平台和应用仍负责：

- 持久化状态及提供程序的可用性。
- 正确的健康检查终结点和降级运行策略。
- 网络安全及传输加密。
- 兼容的发布及状态迁移。
- 容量、过载控制、备份、恢复及事件响应。
