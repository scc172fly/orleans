---
title: 在 Azure App Service 上托管 Orleans
description: 选择并运维 Windows 或 Linux Azure App Service 上受支持的 Orleans 拓扑。
ms.date: 08/21/2026
ms.topic: overview
ms.custom: devops
---

<a id="host-orleans-on-azure-app-service"></a>

# 在 Azure App Service 上托管 Orleans

Azure App Service 可以在专用的多实例计划中托管 Orleans，方法是在每个工作实例上将一个 Silo 与应用程序共同托管。每个工作实例通告自己的专用实例地址和动态分配的专用端口。维护中的示例使用 Azure Table Storage 提供共享的集群成员资格存储和持久化 Grain 状态存储。

根据目标计划的操作系统选择相应指南：

| 目标 | 使用此指南 | 平台特定行为 |
| --- | --- | --- |
| Windows App Service | [将 Orleans 部署到 Windows 上的 Azure App Service](deploy-to-azure-app-service.md) | IIS 集成、Windows 路径与进程模型，以及 Windows App Service 身份验证模块 |
| Linux App Service | [将 Orleans 部署到 Linux 上的 Azure App Service](deploy-to-azure-app-service-linux.md) | 内置的 Linux .NET 技术栈、容器启动时限与路径，以及 App Service 身份验证的 ambassador 边车 |

两篇指南使用相同的应用程序、Bicep 模块、暂存槽工作流、托管标识、健康检查终结点和运维模型。

<a id="supported-topology"></a>

## 受支持的拓扑

维护中的拓扑采用：

- 专用的 Premium v3 App Service 计划，维护中的示例至少包含三个工作实例。
- 每个工作实例上运行一个应用程序进程和一个 Orleans Silo。
- 使用 `WEBSITE_PRIVATE_IP` 和 `WEBSITE_PRIVATE_PORTS` 中逗号分隔的已分配端口列表的第一个端口，作为通告的 Silo 终结点。
- 共同托管本地 Orleans 客户端，并禁用 Orleans 网关。
- 使用 Azure Table Storage 实现集群成员资格和 Grain 状态存储，并通过托管标识进行授权。
- 生产与暂存环境使用不同的集群 ID，但共享稳定的服务 ID。

App Service 前端处理 HTTP 流量。Orleans Silo 之间的连接使用各工作实例的专用终结点。如果需要受信任的外部 Orleans 客户端，应为网关分配并验证第二个专用端口，并确保每个客户端网络都能路由到所有已通告的网关映射。

<a id="qualify-the-target-environment"></a>

## 验证目标环境是否符合要求

App Service 发布示例所使用的专用实例设置。应用程序在启动时验证这些设置是否存在，并通告由此得到的终结点。

应针对所选操作系统、区域、计划层级、网络配置和规模，建立生产环境验证证据：

1. 扩容至至少三个工作实例。
1. 确认每个工作实例均获得独立的专用地址及所需数量的专用端口。
1. 确认每条活动的 Orleans 成员资格记录均与一个工作实例相对应。
1. 测试所有已通告 Silo 终结点之间的双向 TCP 连通性。
1. 在横向扩容、横向缩容、工作实例替换、重启和槽交换期间重复执行这些检查。

区域虚拟网络集成提供工作实例的专用地址及出站虚拟网络路径。已分配的专用端口建立特定于目标环境的入站 Silo 路径。应确保集成子网具有足以应对计划规模、升级和工作实例替换的地址容量。

App Service 将计划中的多个工作实例分布在平台故障域中。若可用性目标包括抵御可用区故障，应在受支持的 Premium 计划上启用 [App Service 可用区冗余](https://learn.microsoft.com/azure/reliability/reliability-app-service)，并确认所选区域、缩放单元、层级和工作实例数量满足平台要求。维护中的示例会创建多个 Premium v3 工作实例，但将可用区冗余留作针对具体环境的生产部署调整项。

<a id="production-checklist"></a>

## 生产环境检查清单

使用详细的 Windows 或 Linux 指南达成以下每项要求。

| 关注事项 | App Service 应达到的结果 |
| --- | --- |
| 拓扑和网络 | 每个工作实例都通告自己的专用实例地址和已分配的 Silo 端口，且每个工作实例都能连接到所有活动成员资格终结点。 |
| 依赖项和数据 | Azure Table 集群成员资格和持久化 Grain 状态使用各环境专用的表。使用提醒器或流功能时，应用程序应显式配置相应提供程序。 |
| 标识和机密 | 为用户分配的托管标识授予最小范围的数据平面角色。App Service 身份验证标识与部署标识保持分离，机密使用受保护的设置或 Key Vault 引用。 |
| 健康状况和生命周期 | 启动预热及 `/health/ready` 在 Orleans 启动后完成。主机关闭前，就绪检查应变为不可用，主机使用 App Service 提供的终止时间窗口。 |
| 扩缩容和复原能力 | 计划保持经过测试的最小工作实例数及备用容量。记录故障域分布及任何必需的可用区冗余设置。在负载下验证扩缩容、实例替换、可用区或工作实例丢失以及依赖项故障时的行为。 |
| 升级和回滚 | 兼容版本在暂存槽中预热，并在槽交换期间加入生产集群。不兼容版本使用独立应用程序和集群 ID。 |
| 可观测性和事件处理 | 将 Application Insights、Log Analytics、Orleans 遥测数据、App Service 实例元数据、成员资格证据和槽操作进行关联。 |
| 基础设施交付 | Bicep 和 GitHub OIDC 工作流创建可复现的基础设施，并通过暂存环境部署不可变的应用程序制品。 |

针对目标环境，完成[生产就绪检查清单](production-readiness.md)、[拓扑、网络和集群](networking.md)、[健康检查与可观测性](health-and-observability.md)、[容量规划与扩缩容](capacity-planning.md)、[优雅关闭与升级](upgrades.md)以及[部署故障排查](troubleshooting-deployments.md)中的指导事项。
