---
title: "部署和作业 Orleans"
description: "计划、部署和操作生产 Orleans 应用软件。"
ms.date: 08/19/2026
ms.topic: overview
---

<a id="deploy-and-operate-orleans"></a>

# 部署和作业 Orleans

Orleans 生产部署是 silo 流程的一组,可选择与单独的 Orleans 客户进行直接沟通。 Silos 通过TCP进行直接沟通。客户通过配置组合组合提供商发现网关并连接到这些网关。

Orleans 管理 grain 启动和集群成员,但托管平台仍然负责流程监督、网络建设、健康探测、秘密、资源分配和控制推出。 生产设计也需要满足申请要求的耐用 grain 状态。

在选择客户、网络、提供者和行政边界进行生产部署之前,先审查 [Orleans 安全模型](../security/index.md) 。

<a id="configure-and-start-a-silo"></a>
<a id="configure-and-connect-to-a-client"></a>
<a id="configure-and-connect-a-client"></a>

<a id="start-the-application"></a>

## 启动应用程序

配置 a silo 排在《公约》 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 与 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*>,然后构建和运行主机 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync*>使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 以同样的方式运行主机。启动主机开始 silo 或连接客户端;停止它 [优美 Orleans 关闭](../host/configuration-guide/shutting-down-orleans.md).

汇编实例见 [服务器配置](../host/configuration-guide/server-configuration.md) 和 [客户配置](../host/configuration-guide/client-configuration.md) 。

<a id="follow-the-deployment-walkthrough"></a>

## 跟着部署行进

[将 Orleans 应用程序安装到 Azure 容器应用软件](../tutorials-and-samples/production-application.md) 将您从一个空目录带到运行中的多进程组,然后通过基于代码的生产配置、部署、可观察性和核查。样本的应用代码、基础设施和部署工作流程一起验证。

对于现有的应用程序,将行进作为序列使用:

1. 东道主主 silos 外部客户和对外客户 .NET 通用主机 。
1. 配置部署提供配置的代码中的群集身份、提供者、证书和终点。
1. 选择一个平台,让每个 silo 有一个唯一、可直接达到的终点。
1. 部署多个 silos ,配备健康探测、遥测、资源政策和优雅的终止期限。
1. 验证成员、TCP可达性、 grain 电话、供应国、失败行为和滚动替换,然后才承认生产流量。

<a id="production-configurations"></a>

<a id="operations-track"></a>

## 业务轨道

将这些条款一并使用:

1. [生产准备状态清单](production-readiness.md) - 审查发射前所需的决定。
1. [运行生产集群](production-operations.md) - 控制配置、进行维护、恢复工作和维护运行簿。
1. [地形学和联网](networking.md) - 配置监听和广告的终点、防火墙和集群。
1. [健康和可观察性](health-and-observability.md) - 设计启动、准备状态、生命力、依赖性健康、遥测和警报。
1. [优优优停机和升级](upgrades.md) - 排水量减少、规模缩小和进行滚动或蓝绿色释放。
1. [能力规划和规模](capacity-planning.md) - 大小 silos,制定资源政策,并验证缩放行为。
1. [备份、恢复和灾后恢复](disaster-recovery.md) - 保护申请状态和安全回收聚类。
1. [处理失败](handling-failures.md) - 设计 grain 要求得出未知的结果、确定是否有效以及约束性重试。
1. [故障解决部署](troubleshooting-deployments.md) - 利用成员资格、联网、依赖关系和遥测手段的逃难事件。

<a id="choose-a-platform"></a>

## 选择平台

使用 [选择部署目标](choose-deployment-target.md) 比较保存的托管服务类型、终端点身份、客户可达性、规模、故障域控制、业务所有权和共同供应商选择。

目标指南包括:

- [Azure Kubernetes 服务](azure-kubernetes-service.md) 和 [platform-neutral Kubernetes](kubernetes.md)。
- [Azure 集装箱套件](deploy-to-azure-container-apps.md),包括其一个silo-每个应用终点模式。
- [Azure 服务处](azure-app-service.md) 日期 时 [视窗](deploy-to-azure-app-service.md) 或 [林勒](deploy-to-azure-app-service-linux.md).
- [服务制造](service-fabric.md).
- [多个东道集装箱](containers.md).
- 满足 [所需平台](platform-guides.md)要求的其他管弦、虚拟机器或赤金属主机。

使用 [将 Orleans 应用程序安装到 Azure 容器应用软件](../tutorials-and-samples/production-application.md)进行有保留应用程序和基础设施样本的制导部署。

应用程序配置见 [服务器配置](../host/configuration-guide/server-configuration.md)、 [客户配置](../host/configuration-guide/client-configuration.md)和 [典型配置](../host/configuration-guide/typical-configurations.md)。

> [!IMPORTANT]
> 本地主机群集和模拟 grain 存储是开发默认。 它们不提供多主机生产组或耐久应用状态 。
