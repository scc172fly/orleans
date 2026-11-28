---
title: 在 Service Fabric 上托管 Orleans
description: 将 Orleans 集群作为应用程序自行实现的 Service Fabric 无状态 Reliable Service 进行托管和运维。
ms.date: 08/03/2026
ms.topic: how-to
ms.custom: devops
---

<a id="host-orleans-on-service-fabric"></a>

# 在 Service Fabric 上托管 Orleans

Orleans 可以在 [Azure Service Fabric](https://learn.microsoft.com/azure/service-fabric) 上作为不分区的无状态 Reliable Service 运行。每个 Service Fabric 服务实例都在普通的 .NET 通用主机中托管一个 Orleans Silo。

Orleans 没有专门用于 Service Fabric 托管或集群成员资格的包。应用程序需自行实现集成，使用：

- [`Microsoft.ServiceFabric.Services`](https://www.nuget.org/packages/Microsoft.ServiceFabric.Services) 提供 Reliable Services 运行时。
- [`Microsoft.Orleans.Server`](https://www.nuget.org/packages/Microsoft.Orleans.Server) 提供 Silo。
- 一个受支持的 [Orleans 集群提供程序](networking.md#choose-a-clustering-provider)，用于成员资格管理和网关发现。

在多个节点、故障域和更新域中运行多个无状态服务实例。不要仅为了获得 Service Fabric 状态复制能力而使用有状态 Reliable Service 托管 Orleans：Orleans Grain 状态由 Orleans 存储提供程序负责，与 Service Fabric 的服务类型无关。

<a id="separate-platform-and-orleans-responsibilities"></a>

## 区分平台与 Orleans 的职责

Service Fabric 和 Orleans 承担互补的职责：

| 关注事项 | 负责方 |
| --- | --- |
| 进程放置、重启、服务实例生命周期、应用程序升级 | Service Fabric |
| 每个实例的端口分配及节点地址 | Service Fabric 服务清单和运行时上下文 |
| Service Fabric 服务终结点发布 | <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.OpenAsync*?displayProperty=nameWithType> 和 Service Fabric Naming Service |
| Silo 成员资格、故障检测、Orleans 网关发现 | Orleans 与所选集群提供程序 |
| Grain 激活和放置 | Orleans |
| 持久化 Grain 状态、提醒器及流 | 已配置的 Orleans 提供程序 |

Service Fabric Naming Service 并不是 Orleans 集群提供程序。Orleans Silo 和客户端必须使用相同的外部集群提供程序、<xref:Orleans.Configuration.ClusterOptions.ServiceId> 和 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。不要在已部署的服务中使用 <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*?displayProperty=nameWithType>。

<a id="implement-the-generic-host-integration"></a>

## 实现通用主机集成

`snippets/service-fabric` 下的示例采用 Azure Table Storage 作为一种具体的集群提供程序，使代码能够端到端编译。根据目标环境的实际需求，可以将它替换为其他受支持的提供程序。

已编译的示例面向 Windows Service Fabric 节点，采用 `win-x64` 自包含发布，因此节点无需另行安装 .NET 运行时。对于 Linux 集群，应选择相应的 Linux 运行时标识符，发布 Linux 可执行文件，并更新服务清单中的入口点。服务生命周期和 Orleans 配置模式保持不变。

服务清单声明两个动态分配的 TCP 终结点：

:::code language="xml" source="snippets/service-fabric/ServiceFabricSilo/PackageRoot/ServiceManifest.xml":::

无状态服务创建一个 <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener>：

:::code language="csharp" source="snippets/service-fabric/ServiceFabricSilo/OrleansStatelessService.cs" id="service_fabric_orleans_stateless_service":::

监听器拥有 Orleans 通用主机。<xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.OpenAsync*?displayProperty=nameWithType> 启动主机，<xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.CloseAsync*?displayProperty=nameWithType> 请求优雅关闭，而 <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.Abort*?displayProperty=nameWithType> 释放主机，但不假定优雅关闭操作能够完成：

:::code language="csharp" source="snippets/service-fabric/ServiceFabricSilo/OrleansCommunicationListener.cs" id="service_fabric_orleans_communication_listener":::

最后，进程注册 Service Fabric 服务类型并构建 Orleans 主机：

:::code language="csharp" source="snippets/service-fabric/ServiceFabricSilo/Program.cs" id="service_fabric_program":::

示例使用：

- 来自 <xref:System.Fabric.NodeContext.IPAddressOrFQDN?displayProperty=nameWithType> 的节点地址作为通告地址。
- 从服务清单分配的端口作为 Silo 和网关终结点的端口。
- `listenOnAnyHostAddress: true`，因为所通告的节点地址可能并不是进程能够直接绑定的地址。
- <xref:Azure.Identity.DefaultAzureCredential> 和 Azure Table 服务 URI，避免在配置中存放存储帐户密钥。
- 120 秒的 .NET 主机关闭超时。

应用程序清单应创建具有多个实例、采用单一分区的无状态服务，并设置 `ServicePackageActivationMode="ExclusiveProcess"`。此示例还将 Orleans 设置、专用本地 RunAs 用户以及 `DefaultAzureCredential` 所使用的服务标识绑定配置在一起：

:::code language="xml" source="snippets/service-fabric/ApplicationPackageRoot/ApplicationManifest.xml":::

独占进程激活避免多个服务实例共享同一个主机进程和终结点资源。应根据容量和冗余需求确定实例数；三个实例只是示例。

<a id="configure-topology-and-endpoints"></a>

## 配置拓扑和终结点

每个服务实例都需要：

- 唯一的 Silo 标识。如果应用程序未设置 <xref:Orleans.Configuration.SiloOptions.SiloName>，Orleans 会生成一个标识。
- 所有其他 Silo 都能够访问的 Silo 终结点。
- 启用网关时，所有 Orleans 客户端都能够访问的网关终结点。
- 与集群提供程序以及所有已配置的状态、提醒器和流提供程序的连通性。

当 _ServiceManifest.xml_ 中的终结点省略 `Port` 时，Service Fabric 会从应用程序端口范围中分配一个端口。应用程序通过 <xref:System.Fabric.CodePackageActivationContext.GetEndpoint*> 读取分配结果。不要硬编码 Orleans 默认端口，除非集群已预留这些端口并能够防止冲突。

`ICommunicationListener.OpenAsync` 返回一个字符串，Service Fabric 通过 Naming Service 发布该字符串。示例将已分配的 Orleans 终结点发布出去用于诊断。Orleans 客户端不使用此值，而是通过 Orleans 集群提供程序发现网关。

应用网络访问控制，仅允许受信任的 Silo 访问 Silo 端口，仅允许受信任的客户端访问网关端口。不要将任一端口直接暴露于公共互联网。当仅依靠网络边界无法提供所需的身份验证和加密时，应配置 [Orleans TLS](../host/transport-layer-security.md)。

<a id="health-and-readiness"></a>

## 健康检查与就绪状态

Service Fabric 在调用 <xref:Microsoft.ServiceFabric.Services.Runtime.StatelessService.RunAsync*?displayProperty=nameWithType> 之前打开通信监听器。在此集成中，`OpenAsync` 仅在 Orleans 主机启动之后才完成，因此 Silo 仍在启动时，Service Fabric 不会发布监听器地址。

这一生命周期边界不可或缺，但并不能构成完整的应用程序健康模型。对于运维人员或受监视的升级必须评估的持续性状况，应用程序应自行生成 Service Fabric [健康报告](https://learn.microsoft.com/azure/service-fabric/service-fabric-health-introduction)，例如：

- 无法加入预期的 Orleans 集群。
- 丢失必需的存储或集群成员资格依赖项。
- 本地资源饱和或无法继续推进工作。
- 长时间处于降级模式。

避免短暂的依赖项故障导致多个实例同步重启。如果进程还提供 HTTP 入站流量入口，应按照[健康检查与可观测性](health-and-observability.md)中的说明，分别实现启动、就绪、存活和依赖项检查。

根据应用程序信号设置 Service Fabric 升级健康策略。平台内置健康监测能够发现进程及部署故障，但无法推断 Orleans 请求的正确性或服务容量。

<a id="shutdown-and-scale-in"></a>

## 关闭与横向缩容

在无状态服务优雅关闭期间，Service Fabric 调用 `ICommunicationListener.CloseAsync`。监听器调用 <xref:Microsoft.Extensions.Hosting.IHost.StopAsync*>，使 Orleans 能够在给定的截止时间内退出集群成员资格并停止运行。

优雅关闭并不保证一定发生：

- 进程、节点或生命周期发生故障后，可能调用 `Abort`。
- Service Fabric 可能在配置的超时之后终止代码包。
- 主机可能在退出集群成员资格之前崩溃或失去网络连接。

因此，系统的正确性必须能够容忍 Silo 突然丢失和[调用结果不确定](handling-failures.md)的情况。将 Service Fabric 的关闭和升级超时配置为大于实测的 Orleans 关闭耗时，并使其与 <xref:Microsoft.Extensions.Hosting.HostOptions.ShutdownTimeout> 保持一致。

如有可能，每次仅对一个实例或一个更新域执行横向缩容。等待 Orleans 成员资格状态及应用程序延迟趋于稳定，再进一步减少容量。

<a id="rolling-upgrades"></a>

## 滚动升级

使用 Service Fabric 受监视的滚动升级，使每个更新域都必须满足应用程序健康策略后，下一个更新域才能继续升级。在一个更新域之外保留足够的实例，以处理工作负载并承接重新激活的 Grain。

滚动升级期间，新旧 Silo 同时存在。它们必须在所有边界上保持兼容：

- Grain 接口和序列化有效载荷。
- 已持久化的 Grain 状态。
- 集群成员资格、提醒器、流和存储架构。
- 外部副作用及去重记录。

发布新版本时，递增应用程序类型版本和服务清单版本。还需递增每个发生变更的代码包、配置包或数据包的版本；仅修改二进制文件而不改变 `CodePackage` 版本，Service Fabric 无法将其识别为新的代码包。测试混合版本以及新版本已写入状态时的自动回滚行为。请参阅[优雅关闭与升级](upgrades.md)和 [Service Fabric 应用程序升级](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-upgrade)。

对于不兼容版本，使用单独命名的 Service Fabric 应用程序和不同的 Orleans `ClusterId`，然后遵循[蓝绿升级指南](upgrades.md#blue-green-upgrades)。不要让不兼容的集群同时拥有相同的可变 Grain 状态。

<a id="identity-secrets-and-configuration"></a>

## 标识、机密与配置

两种标识的用途不同：

- **RunAs 标识**是代码包使用的本地操作系统帐户。示例创建专用本地用户并应用 `RunAsPolicy`，而不是以 Service Fabric 默认帐户运行。
- **托管标识**用于向 Azure 资源验证应用程序的身份。示例将应用程序清单中的 `OrleansSiloApplicationIdentity` 映射到服务清单中的 `OrleansSiloServiceIdentity`。

仅有清单映射并不会创建或分配 Azure 标识。示例配置使用**用户分配的标识**。将应用程序部署为 Azure 资源，在 Azure Resource Manager 部署中分配用户分配的标识，并将其友好名称映射到 `OrleansSiloApplicationIdentity`。未作为 Azure 资源部署的应用程序无法使用 Service Fabric 应用程序托管标识。请参阅[使用用户分配的托管标识部署 Service Fabric 应用程序](https://learn.microsoft.com/azure/service-fabric/how-to-deploy-service-fabric-application-user-assigned-managed-identity)。

系统分配的标识使用保留的应用程序标识名称 `SystemAssigned`；若选择此模型，应相应更新应用程序主体及标识绑定策略。

仅向托管标识授予提供程序实际所需的权限。对于示例中的 Azure Table 集群提供程序，应在实际可行的最小范围内分配包含表数据操作权限的角色，例如 **Storage Table Data Contributor**（存储表数据参与者）。管理平面的 Contributor（参与者）角色不授予表数据访问权限。

不要将凭据放入源代码、清单、应用程序参数或命令行。无法使用工作负载标识时，应使用外部机密存储或 [Service Fabric 加密机密](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-secret-management)，并规划机密轮换及过期告警。

将 `ServiceId` 视为稳定的应用程序标识，将 `ClusterId` 视为环境或部署标识。示例在服务清单中声明非机密默认值，并在应用程序清单中声明参数化的环境覆盖值。部署时替换示例中的表 URI，并设置各环境专用的应用程序参数。在应用程序升级时保留实际生效的参数映射，因为 Service Fabric 不会自动将应用程序参数传递至新版本。启动主机之前验证实际生效的配置；缺少必要值时，应明确使启动失败。

<a id="diagnostics"></a>

## 诊断

关联以下信息：

- Service Fabric 应用程序、服务、分区、实例、节点、代码包和版本。
- Orleans 服务 ID、集群 ID、Silo 名称及已通告的终结点。
- 部署的更新域及应用程序升级操作。

使用 Service Fabric Explorer 和健康事件检查放置、重启、包激活、终结点分配和升级决策。按照 [Orleans 可观测性](../host/monitoring/index.md)中的说明导出 Orleans 日志、指标和跟踪数据。将遥测数据保存在 Service Fabric 集群之外，以便集群发生事件时仍可使用这些数据。

发生事件时，将监听器发布的终结点与 Orleans 成员资格记录进行比较，并测试实际通告的 Silo 和网关地址。请参阅[部署故障排查](troubleshooting-deployments.md)。
