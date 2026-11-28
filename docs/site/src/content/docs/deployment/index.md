---
title: 部署与运维 Orleans
description: 规划、部署并运维生产环境中的 Orleans 应用程序。
ms.date: 08/19/2026
ms.topic: overview
---

<a id="deploy-and-operate-orleans"></a>

# 部署与运维 Orleans

Orleans 的生产部署由一组 Silo 进程组成集群，也可以包含独立的 Orleans 客户端。Silo 之间通过 TCP 直接通信。客户端通过配置的集群提供程序发现网关，并连接到这些网关。

Orleans 负责管理 Grain 激活和集群成员资格，但托管平台仍负责进程监督、网络、健康探测、机密信息、资源分配以及受控发布。生产环境的设计还应根据应用程序需求提供持久化 Grain 状态。

在确定生产部署的客户端、网络、提供程序和管理边界之前，请先阅读 [Orleans 安全模型](../security/index.md)。

<a id="configure-and-start-a-silo"></a>
<a id="configure-and-connect-to-a-client"></a>
<a id="configure-and-connect-a-client"></a>
<a id="start-the-application"></a>

## 启动应用程序

使用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 在 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 上配置 Silo，然后构建主机并使用 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync*> 运行它。对于独立的客户端进程，使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>，并以相同方式运行其主机。启动主机会启动 Silo 或连接客户端；停止主机会协调 [Orleans 优雅关闭](../host/configuration-guide/shutting-down-orleans.md)。

有关可编译的示例，请参阅[服务端配置](../host/configuration-guide/server-configuration.md)和[客户端配置](../host/configuration-guide/client-configuration.md)。

<a id="follow-the-deployment-walkthrough"></a>

## 按照部署演练操作

[将 Orleans 应用程序部署到 Azure Container Apps](../tutorials-and-samples/production-application.md) 引导你从空目录构建并运行多进程集群，再完成基于代码的生产配置、部署、可观测性设置及验证。示例的应用程序代码、基础设施和部署工作流会一同验证。

对于现有应用程序，可按演练中的顺序操作：

1. 在 .NET 通用主机上托管 Silo 和外部客户端。
1. 在代码中依据部署提供的配置设置集群标识、提供程序、凭据和端点。
1. 选择能够为每个 Silo 提供唯一且可直接访问的端点的平台。
1. 部署多个 Silo，并配置健康探测、遥测、资源策略及优雅终止的截止时间。
1. 在接入生产流量前，验证成员资格、TCP 连通性、Grain 调用、提供程序状态、故障处理行为及滚动替换。

<a id="production-configurations"></a>
<a id="operations-track"></a>

## 运维指南

结合使用以下文章：

1. [生产就绪检查清单](production-readiness.md) - 审查上线前必须作出的决策。
1. [运维生产集群](production-operations.md) - 管理配置、执行维护、演练恢复并维护运维手册。
1. [拓扑与网络](networking.md) - 配置侦听端点、公布端点、防火墙和集群管理。
1. [健康检查与可观测性](health-and-observability.md) - 设计启动、就绪、存活、依赖项健康检查、遥测和告警。
1. [优雅关闭与升级](upgrades.md) - 排空实例、缩容及执行滚动或蓝绿发布。
1. [容量规划与伸缩](capacity-planning.md) - 确定 Silo 规格、设置资源策略并验证伸缩行为。
1. [备份、还原与灾难恢复](disaster-recovery.md) - 保护应用程序状态并安全恢复集群。
1. [故障处理](handling-failures.md) - 针对未知结果、幂等性和有界重试设计 Grain 调用。
1. [部署故障排查](troubleshooting-deployments.md) - 根据成员资格、网络、依赖项和遥测对故障进行初步排查。

<a id="choose-a-platform"></a>

## 选择平台

通过[选择部署目标](choose-deployment-target.md)比较持续维护的托管拓扑、端点标识、客户端可达性、伸缩能力、故障域控制、运维责任及常见的提供程序选项。

部署目标指南涵盖：

- [Azure Kubernetes Service](azure-kubernetes-service.md) 和[与平台无关的 Kubernetes](kubernetes.md)。
- [Azure Container Apps](deploy-to-azure-container-apps.md)，包括其每个应用一个 Silo 的端点模型。
- [Azure App Service](azure-app-service.md)，运行于 [Windows](deploy-to-azure-app-service.md) 或 [Linux](deploy-to-azure-app-service-linux.md)。
- [Service Fabric](service-fabric.md)。
- [跨多个主机的容器](containers.md)。
- 满足[平台要求](platform-guides.md)的其他编排器、虚拟机或裸机主机。

如需结合持续维护的应用程序和基础设施示例进行部署，请参阅[将 Orleans 应用程序部署到 Azure Container Apps](../tutorials-and-samples/production-application.md)。

有关应用程序配置，请参阅[服务端配置](../host/configuration-guide/server-configuration.md)、[客户端配置](../host/configuration-guide/client-configuration.md)和[典型配置](../host/configuration-guide/typical-configurations.md)。

> [!IMPORTANT]
> 本地主机集群和内存 Grain 存储是开发环境的默认配置，不能提供跨主机的生产集群或持久化应用程序状态。
