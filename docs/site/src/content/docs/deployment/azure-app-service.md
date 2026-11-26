---
title: "东道主主 Orleans 日期 时 Azure 服务处处长"
description: "选择并操作支持的 Orleans Windows 或 Linux 上的地貌学 Azure 服务处。"
ms.date: 08/21/2026
ms.topic: overview
ms.custom: devops
---

<a id="host-orleans-on-azure-app-service"></a>

# 东道主主 Orleans 日期 时 Azure 服务处处长

Azure 应用程序服务可托管 Orleans ,办法是共同主办一个 silo ,在专门的多企业计划中对每个工人提出申请。每个工人都宣传其私人实例地址和动态分配的私营港口。 Azure 表格存储提供所保持样本中的共享集群成员以及耐用 grain 状态。

选择目标计划操作系统指南:

| 具体目标具体目标 | 使用此指南 | 平台特定行为 |
| --- | --- | --- |
| Windows App 服务 | [在 Windows 上部署 Orleans 到 Azure 应用程序服务](deploy-to-azure-app-service.md) | IIS集成、Windows路径和流程模型以及WindowsApp服务认证模块 |
| Linux Apper 服务 | [部署 Orleans 到 Linux 上的 Azure 应用服务](deploy-to-azure-app-service-linux.md) | 内置Linux .NET 堆叠、集装箱启动期限和路径以及App Servication App Servication代理大使随车 |

这两本指南使用相同的应用程序、Bicep模块、中转站点工作流程、管理身份、健康终点和业务模式。

<a id="supported-topology"></a>

## 支助的地形表

维持的地表学用途:

- 专门制定的Premium v3 App服务计划,保留样本中至少有三名工人。
- 每名工人有一个申请过程和一个 Orleans silo 。
- `WEBSITE_PRIVATE_IP` 和以逗号分隔的 `WEBSITE_PRIVATE_PORTS` 分配的第一个端口作为公布的 silo 终点。
- 共同托管的本地 Orleans 客户端, 且 Orleans 网关已禁用。
- Azure 用于集聚和 grain 状态的表格储存,经管理身份授权。
- 单独制作和中转集束识别码,并有一个共同的稳定服务识别码。

应用服务处前端处理 HTTP 传输。 Orleans silo 使用每个工人的私人端点。如果信任外部 Orleans 客户需要、分配和验证门户的第二个私人港口,并确保每个客户网络都能通过每一个广告网关图绘制。

<a id="qualify-the-target-environment"></a>

## 目标环境的定性

App Service公布了样本使用的私人实例设置,应用程序验证了这些样本在启动期间的存在,并宣传由此产生的终点。

建立关于选定操作系统、区域、计划层级、网络配置和规模的生产证据:

1. 规模至少为三名工人。
1. 确认每个工人都收到一个单独的私人地址和所需的私人港口点票。
1. 将每个活跃 Orleans 成员与一个工人实例匹配。
1. 每个广告的 silo 终点之间测试双向 TCP连接。
1. 在扩大规模、按比例调整、工人更换、重新启动和换换职位期间,重复检查。

区域虚拟网络一体化区域虚拟网络提供私人工人地址和出境的虚拟网络路径。 silo 路径。保持集成子网的大小,以适应计划的规模、升级和工人替换。

App Service 将多个计划工人分布于平台故障域。 对于包括区故障在内的可用目标,允许使用支持的钚计划 [服务区冗余](https://learn.microsoft.com/azure/reliability/reliability-app-service) ,并确认选定区域、规模单位、层级和工人计数符合平台要求。 保存的样本创造了多个Premium v3工人,但作为环境特定生产适应而留下区冗余。

<a id="production-checklist"></a>

## 生产清单

使用详细的 Windows 或 Linux 指南来完成每个结果 。

| 关注问题 | 服务结果 |
| --- | --- |
| 地形学和联网 | 每个工人都登广告,宣传其私人网站地址并分配 silo 港口,每个工人都可以与每一个积极成员端点连接。 |
| 依赖和数据 | Azure 表群和耐用 grain 状态使用环境特定表格。应用程序在使用这些特征时明确配置提醒和流源提供者。 |
| 身份和机密 | 用户指定的管理身份具有狭窄的数据机作用,应用程序服务认证和部署身份仍然分开,机密使用受保护的环境或关键跳跃引用。 |
| 健康和生命周期 | 启动热和 `/health/ready` 完成后 Orleans 启动。在主机关闭前,准备状态将无法可用,主机使用可用的 AppService 终止间隔。 |
| 规模和复原力 | 该计划保留了经过测试的最低工人数和剩余能力,记录了过失域分布和任何必要的区冗余。 规模、更换、区或工人损失以及依赖性失职行为在负载下得到验证。 |
| 升级和回调 | 兼容释放在中转位置热能释放,并在交换时加入生产组群。不兼容释放使用单独的应用程序和集束识别码。 |
| 观察和事件 | 应用程序透视、日志分析、 Orleans 遥测、应用服务实例元数据、成员资格证据和空档业务是相互关联的。 |
| 基础设施交付 | Bicep和OIDC的 GitHub 工作流程创建可复制的基础设施,并通过中转部署不可变应用文物。 |

完成目标环境的 [生产准备状态清单](production-readiness.md)、 [地形学、联网和集群](networking.md)、 [健康和可观察性](health-and-observability.md)、 [能力规划和规模](capacity-planning.md)、 [优优优停机和升级](upgrades.md)和 [故障解决部署](troubleshooting-deployments.md) 指南。
