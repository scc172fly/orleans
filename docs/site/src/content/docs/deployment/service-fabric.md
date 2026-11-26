---
title: "服务硬件主机 Orleans"
description: "主办并运行一个 Orleans 组,作为申请书用户服务机构。"
ms.date: 08/03/2026
ms.topic: how-to
ms.custom: devops
---

<a id="host-orleans-on-service-fabric"></a>

# 服务硬件主机 Orleans

Orleans 能够运行 [Azure 服务制造](https://learn.microsoft.com/azure/service-fabric) 每个服务局都设有一个服务机构 Orleans silo 正常 .NET 通用主机 。

没有 Orleans 服务制造主机或组合软件包。

- [`Microsoft.ServiceFabric.Services`](https://www.nuget.org/packages/Microsoft.ServiceFabric.Services) 可靠服务是运行的。
- [`Microsoft.Orleans.Server`](https://www.nuget.org/packages/Microsoft.Orleans.Server) 排排 silo.
- 一个支持 [Orleans 集群集组提供商](networking.md#choose-a-clustering-provider) 加入和网关发现。

使用多个无国籍服务实例,覆盖节点、断层域和更新域。不要使用一个状态可靠的服务托管 Orleans 仅仅为了获得服务制造州复制: Orleans grain 州使用一个 Orleans 存储供应商,而独立于服务组合服务类型。

<a id="separate-platform-and-orleans-responsibilities"></a>

## 独立的平台和 Orleans 责任

服务制造和 Orleans 具有互补作用:

| 关注问题 | 所有人 |
| --- | --- |
| 流程安放、重新启动、服务实例生命周期、应用程序升级 | 服务制造 |
| 港口分配和节点地址 | 服务基础服务 服务显示和运行时间 |
| 服务基础服务端点出版物 | <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.OpenAsync*?displayProperty=nameWithType> 和事务处制作命名处 |
| Silo 成员、故障检测和 Orleans 网关发现 | Orleans 和选定的集群提供商 |
| Grain 启动和安置 | Orleans |
| 持久 grain 状态、提醒和流 | 配置的 Orleans 服务提供方 |

服务制造命名服务不是 Orleans 集群提供者。 Orleans silos 客户必须使用同样的外部集群供应商、 <xref:Orleans.Configuration.ClusterOptions.ServiceId>和 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。在部署的服务中,不要使用 <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*?displayProperty=nameWithType> 。

<a id="implement-the-generic-host-integration"></a>

## 实施通用主机集成

`snippets/service-fabric` 下的样本使用 Azure 表存储作为混凝土集成提供商,以便代码汇编结束。在适合环境时用另一个支持的提供者替换。

汇编的范例针对Windows Services公司制造节点,并出版 `win-x64`,所以节点不需要单独安装 .NET 运行时时间。对于 Linux 集集,请选择相应的 Linux 运行时间标识符,发布一个 Linux 可执行文件,并更新服务列表切入点。保持使用寿命周期和 Orleans 配置模式不变。

服务单宣布两个动态分配的TCP终点:

:::code language="xml" source="snippets/service-fabric/ServiceFabricSilo/PackageRoot/ServiceManifest.xml":::

无国籍服务产生 <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener>:

:::code language="csharp" source="snippets/service-fabric/ServiceFabricSilo/OrleansStatelessService.cs" id="service_fabric_orleans_stateless_service":::

听众拥有 Orleans 通用主机 。 <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.OpenAsync*?displayProperty=nameWithType> 开始 <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.CloseAsync*?displayProperty=nameWithType> 要求优雅关闭, <xref:Microsoft.ServiceFabric.Services.Communication.Runtime.ICommunicationListener.Abort*?displayProperty=nameWithType> 处置时不假定工作优雅即可完成:

:::code language="csharp" source="snippets/service-fabric/ServiceFabricSilo/OrleansCommunicationListener.cs" id="service_fabric_orleans_communication_listener":::

最后,该流程登记了服务处的制造服务类型,并建造了 Orleans 主机:

:::code language="csharp" source="snippets/service-fabric/ServiceFabricSilo/Program.cs" id="service_fabric_program":::

举例使用:

- 广告地址为 <xref:System.Fabric.NodeContext.IPAddressOrFQDN?displayProperty=nameWithType> 中的节点地址。
- 服务单中为 silo 和网关端点分配的港口。
- `listenOnAnyHostAddress: true` ,因为公布的节点地址可能不是地址,因此该程序可以直接约束。
- <xref:Azure.Identity.DefaultAzureCredential> with an Azure Table service URI, avoiding a storage account key in configuration.
- 120秒 .NET 主机关闭超时

应用程序单项应创建单一的无国籍服务,并有多个实例和 `ServicePackageActivationMode="ExclusiveProcess"`。此示例还连接 Orleans 设置、专用本地 RunAs 用户以及 `DefaultAzureCredential`所使用的服务身份约束:

:::code language="xml" source="snippets/service-fabric/ApplicationPackageRoot/ApplicationManifest.xml":::

启动专有进程避免在服务中共享一个主机程序和终端资源。 大小能力和冗余的实例数; 三个仅是一个例子。

<a id="configure-topology-and-endpoints"></a>

## 配置表层和终点

每项服务实例需要:

- 独一 silo 身份。 Orleans 如果应用程序不设置, 程序会生成一个 <xref:Orleans.Configuration.SiloOptions.SiloName>.
- A silo 可以从其他方达到的终点 silo.
- 启用网关时, 每个 Orleans 客户端都可以达到的网关端点 。
- 与集群提供商和每个配置状态、提醒和流提供商的连接。

当一个端点省略 `Port` 内 _ServiceManifest.xml_,“服务结构”从应用端端口范围分配一个港口。 <xref:System.Fabric.CodePackageActivationContext.GetEndpoint*>不要硬码 Orleans 除非组合组合组合保留了默认端口并防止冲突。

`ICommunicationListener.OpenAsync` 返回一个字符串,由服务 Fabric 通过其命名服务发布。样本公布分配给诊断的 Orleans 终点。 Orleans 客户不消耗该值;他们通过 Orleans 组合提供商发现网关。

应用只信任的网络控制 silos 才能到达 silo 端口, 只有受信任客户才能到达网关端口。 不要直接将任何一个端口暴露在公共互联网上。 当网络边界本身不提供所需的认证和加密时配置 [Orleans TLS 电信](../host/transport-layer-security.md) 。

<a id="health-and-readiness"></a>

## 健康和准备状态

服务包在拨打 <xref:Microsoft.ServiceFabric.Services.Runtime.StatelessService.RunAsync*?displayProperty=nameWithType>之前打开了通信听众。在这一整合中, `OpenAsync` 只有在 Orleans 主机启动后才完成,所以服务包不会在启动 silo 时发布听众地址。

使用寿命周期边界是必要的,但并非一个完整的应用健康模型。添加应用程序用户服务 Fabric [健康报告](https://learn.microsoft.com/azure/service-fabric/service-fabric-health-introduction) 操作者或监测的升级必须评估以下等持续条件:

- 未加入预定的 Orleans 组群。
- 失去所需的储存或集群依赖。
- 当地饱和或无法取得进展。
- 一种长期退化的模式。

避免瞬时性依赖失灵引发同步重开。 如果该过程还暴露 HTTP 入侵, 则按 [健康和可观察性](health-and-observability.md)所述实施单独的启动、 准备状态、 活性以及抚养检查 。

Set Service Fabric 将健康政策从应用信号上升级。 内建平台健康可以检测过程和部署故障, 但无法推断 Orleans 要求正确性或能力。

<a id="shutdown-and-scale-in"></a>

## 关闭和缩放

在优雅的无国籍服务停业期间,服务公司Fabric呼叫 `ICommunicationListener.CloseAsync`。听众呼叫 <xref:Microsoft.Extensions.Hosting.IHost.StopAsync*>,允许 Orleans 离开成员资格并在规定的期限内停止工作。

优雅的关闭不能保证:

- `Abort` 可在过程、节点或生命周期失效后发生。
- 服务配置可以在配置超时后终止代码包 。
- 东道主在退出会籍前可能崩溃或失去网络接入。

因此,正确性必须能够容忍突然 silo 损失和 [未知的通话结果](handling-failures.md)。配置服务结构关闭和更新超时,超过所计量的 Orleans 停机期限,并与 <xref:Microsoft.Extensions.Hosting.HostOptions.ShutdownTimeout>保持一致。

在一个实例中缩放, 或者在可能的时间更新域。 等待 Orleans 成员资格和应用延迟以稳定下来, 然后移除更多能力 。

<a id="rolling-upgrades"></a>

## 滚式升级

使用服务局监测了滚动升级,因此每个更新域必须在下一个运行之前满足应用卫生政策。在更新域之外保留足够多的事例,以完成工作量并吸收重新启动的 grains。

新的和旧的 silos 在滚动升级期间共存。

- Grain 接口和序列式有效载荷。
- 状态 grain
- 集束、提醒、流和存储计划。
- 外部副作用和重复记录。

添加发布应用程序类型版本和服务表格式。 另外,还加插每个修改过的代码、配置或数据包的版本; 更改二进制而不修改 `CodePackage` 版本并不表示服务软件包的新代码包。 测试以混合版本和新版本撰写状态自动回滚。 见 [优优优停机和升级](upgrades.md) 和 [服务制造应用程序升级](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-upgrade)。

对于不兼容的释放,请使用单独命名的“服务制造”应用程序和单独的 Orleans `ClusterId`,然后跟随 [蓝绿色指导指南](upgrades.md#blue-green-upgrades)。不要让不兼容的组群同时拥有相同的变异状态 grain 。

<a id="identity-secrets-and-configuration"></a>

## 身份、秘密和配置

两种身份有不同的目的:

- **RunAs 身份** 是代码包的本地操作系统账户。 样本创建了专门的本地用户, 并应用了一个 `RunAsPolicy` 而不是在服务 Fabric 默认账户下运行 。
- 一种 **管理下的身份** 认证对 Azure 资源的申请。在申请表上,样本映射了 `OrleansSiloApplicationIdentity` ,显示为服务清单中的 `OrleansSiloServiceIdentity` 。

光是地标绘图 并不创造或分配 Azure 身份。为 **用户指定身份**.将申请作为 Azure 中指定用户指定的标识 Azure 资源管理员部署,并绘制友好姓名地图 `OrleansSiloApplicationIdentity`.未在 Azure 资源无法使用服务应用程序管理身份 。 [部署一个带有用户指派管理身份的服务硬件应用程序](https://learn.microsoft.com/azure/service-fabric/how-to-deploy-service-fabric-application-user-assigned-managed-identity).

系统指定身份使用保留应用程序的识别名称 `SystemAssigned`;如果选择该模式,则相应更新应用程序的主要和有身份约束力的政策。

仅授予其需要的供应商管理身份。 对于样本 Azure 表群集提供者,指定一个角色,在最狭窄的实际范围内包含表格数据行动,如 **存储表数据贡献者**。管理-飞机贡献者的作用不准许访问表格数据。

工作身份不存在时,请使用外部秘密商店或 [服务制造加密秘密](https://learn.microsoft.com/azure/service-fabric/service-fabric-application-secret-management),并计划轮换和到期通知。

将 `ServiceId` 视为稳定的应用程序身份, `ClusterId` 则视为环境或部署身份。 样本中声明服务状态表中的非绝密缺省, 并指定了应用状态单中的参数环境为超值。 替换示例表 URI 并在部署时设定了特定环境的应用程序参数。 在应用程序升级期间保留有效的参数地图, 因为 Fabric 服务不会自动将应用程序参数向前移动。 在启动主机之前验证有效配置, 当需要值时, 明确启动失败 。

<a id="diagnostics"></a>

## 诊断

相关日期 :

- 服务性制造应用程序、 服务、 分区、 实例、 节点、 代码包和版本 。
- Orleans 服务标识、集束识别码、 silo 名称和广告的终点。
- 更新部署域和应用升级操作。

使用服务包件探索器和健康事件检查放置、重新启动、启动软件包、终端分配和升级决定。出口 Orleans 日志、度量和跟踪 [Orleans 易观测](../host/monitoring/index.md)所述 。在服务包件集群之外保留遥测,以便在集束事故期间仍然可用。

将听众发表的结束点与 Orleans 测试广告的准确内容 silo 和网关地址。 [故障解决部署](troubleshooting-deployments.md).
