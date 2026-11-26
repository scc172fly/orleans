---
title: "东道主主 Orleans 日期 时 Azure 集装箱集装箱"
description: "设计、安全、部署和验证 Orleans 组群一一 Azure 集装箱装货箱"
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="host-orleans-on-azure-container-apps"></a>

# 东道主主 Orleans 日期 时 Azure 集装箱集装箱

Azure 集装箱应用软件主机 Orleans ,使用保存 Orleans 端点语义的地形学。 Orleans silos 连接到每个成员广告的唯一 silo 和网关端点。集装箱应用程序服务于发现和TCP或HTTP反面地址为 **集装箱应用软件或修改** ,并覆盖其复制品的负载平衡。因此,保持的生产型号指定了每个 silo 自己的容器应用程序和一个复制件。

使用虚拟网络一体化的内部环境及其私人静态IP。 silo 单独作为单独的集装箱应用软件, 精确复制一个副本并给每个应用程序分配一对独特的 TCP 入侵端端口, 以该私有 IP 。 请重复此资源添加 silos,然后完成本页的制作验收测试。 silo 一个集装箱应用软件的复制件不属于这种维持部署形状,因为集装箱应用程序公布应用程序和修订端点,而不是支持的每个复制端点标识。

使用 [部署和规模 a Orleans 应用 Azure](../quickstarts/deploy-scale-orleans-on-azure.md) 快速启动来学习 Azure 开发者 CLI 部署流程。 [Orleans 组群一一 Azure 试样](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps) 也可用于研究多成分表层学和外部标尺合同。在调整任一生产资源之前先审查 [样本限制](#understand-the-sample) 。

如果应用程序使用 Aspire , [Azure 集装箱自动装件的快速部署流量](https://aspire.dev/deployment/azure/container-apps/) 可以提供并发布平台资源。 它不会删除本页的 Orleans 端点要求: 审查生成的表层学, 保持每张广告的 silo 端点的复制一份, 配置外部组合和耐久状态, 并在生产前添加所需的健康、 身份、 升级和能力控制 。

<a id="choose-a-topology"></a>

## 选择一个表层

| 地形 | 终点行为 | 指导指南 |
| --- | --- | --- |
| 每个容器应用一个 silo 复制 | 环境私人静态IP线路上独有的 silo 和网关端口对,一个应用和一个 silo。 | 当需要集装箱应用程序时使用此表层。 设定一个软件的最低和最大复制件, 并安装多个 silo 应用程序 。 |
| 一个容器应用中多个 silo 复制件 | 应用程序和修改主机名路径, 通过平台代理程序进行任何符合资格的复制。 容器应用程序发布不支持选择一个复制的地址 。 | 使用每个应用的维持的一复制版表层,或选择直接地址平台,如AKS。 |
| Silos 日期 时 Kubernetes 或另一个直接地址平台 | 管弦乐队为每个舱或过程发布一个可变地址。 | 当自动复制规模和常规滚动替换是需要时,更倾向于这种地形。 |

单复制的每套应用地形在操作上更为严重, 因为基础设施作为代码必须创建一套封闭 silo 应用程序并分配环境专用港口。 在升级过程中还需要有故意更换程序。 作为交换, 它并不依赖于无证舱位网络 。

多个复制应用程序提供程序和应用程序冗余, 但集装箱应用程序没有记录不同应用程序之间的反亲和跨区设置。 其区域冗余指导分发多个相同的应用程序复制件, 需要至少两个复制件, 这与此端点模式相冲突。 不要为每套复制的图像要求可用区冗余。 如果需要独立的故障保留位置, 请使用一个平台, 直接提供每个进程地址和配置控制 。

将HTTP APP公开的 PIP 保存在单独的集装箱应用程序中,该软件主 Orleans 客户端, 或者共同主办 HTTP API 与每个 silo 只有当该表层是故意的时, 公众 HTTP 入侵路径应用请求; 它不是 Orleans 运输。

<a id="configure-networking-and-orleans-endpoints"></a>

## 配置网络和 Orleans 端点

在现有的虚拟网络中创建集装箱应用环境,其无障碍级别设于 **内部**。然后该环境在 Azure 内部负载平衡器上有一个私有静态IP,没有公共端点。

配置两个 TCP 反向端口, 每个 silo 应用程序都使用 :

- **silo 港口** 载有 silo-至silo 的交通量。
- **网关端** 载有来自 Orleans 客户的交通。

设置 App 反向 `external: true` 使这些港口可以在环境边界上使用。由于环境本身是内部的,因此进入的知识产权仍然对虚拟网络保持私有。指定一个独特的曝光点。 silo 和每端的网关, silo a. 外部暴露的TCP港口必须在整个环境中独有; `36985` 每个应用程序都可以使用相同的集装箱目标港口,例如 `11111` 和 `30000`.

将客户置于同一环境中或连接到环境的私人IP的私营网络中。 限制网络访问可信赖的工作量, 当网络不是信任边界时使用 [Orleans 运输层安全](../host/transport-layer-security.md) 。

环境的 `properties.staticIp` 以及应用软件的独特港口对每个 silo将收听端关卡装在集装箱目标港口:

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_endpoint_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_endpoints":::

**听端点** 是程序在容器内绑定的。 **广告的终点** 是 Orleans 给同行和客户的会员写给会员的。 与 `0.0.0.0` 连接后找不到广告地址。 如果反向绘制不同曝光和目标端口地图, 请直接配置 <xref:Orleans.Configuration.EndpointOptions> :

- <xref:Orleans.Configuration.EndpointOptions.SiloListeningEndpoint> and <xref:Orleans.Configuration.EndpointOptions.GatewayListeningEndpoint> contain the target ports.
- <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress>, <xref:Orleans.Configuration.EndpointOptions.SiloPort>, and <xref:Orleans.Configuration.EndpointOptions.GatewayPort> contain the routable ingress address and exposed ports.

容器应用程序提供 `CONTAINER_APP_REPLICA_NAME` 来识别复制件和 `CONTAINER_APP_HOSTNAME` 来识别修改主机。 支持的每个复制网络地址都不是任何值。 不要从多个 silo 复制件中登广告或修改主机名称: 一个对等点可以选择到与成员条目所指定的不同复制件。

基本网络要求见 [Azure 集装箱应用程序中的联网](https://learn.microsoft.com/azure/container-apps/networking)、 [配置内嵌配置](https://learn.microsoft.com/azure/container-apps/ingress-how-to)、 [运行 Orleans 多个主机的集装箱](containers.md)和 [地形学、联网和集群](networking.md) 。

<a id="configure-clustering-and-durable-state"></a>

## 配置组合和持久性状态

使用外部集群提供者,使 silos 和客户发现相同的成员记录。 Azure 表格存储是一种常见的选择。一个集群提供者储存成员资格;它不持续 grain 状态。为必须生存下来或损失的每个国家分别登记一个 grain 存储供应商。

以下配置使用 Microsoft Entra ID 代替存储账户密钥:

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_apps_storage_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_apps_storage":::

为此配置安装 `Microsoft.Orleans.Clustering.AzureStorage`、 `Microsoft.Orleans.Persistence.AzureStorage`和 `Azure.Identity` 。配置与 `ServiceId`、 `ClusterId`、组合表和证书相同的 Orleans 客户端。保持 `ServiceId` 稳定,用于应用程序,并为每个环境或孤立的蓝绿色集群使用单独的 `ClusterId` 。

仅在表格或存储账户上指定 silo 和客户名 **存储表数据贡献器** 。 Orleans Azure 表格提供者呼叫 <xref:Azure.Data.Tables.TableClient.CreateIfNotExistsAsync*?displayProperty=nameWithType>,因此单是 **存储表数据阅读器** 是不够的,即使用户随后读取网关记录也是如此。如果 grain 国家使用 blobs 或其他服务,则在最狭窄的实际范围内授予相应的数据机功能。当每个消费者支持 Microsoft Entra ID时,禁用共享密钥访问。

<a id="provision-production-infrastructure"></a>

## 提供生产基础设施

使用稳定的 Azure 资源管理员 API 版本,定义环境和每个容器应用程序。

- 虚拟网络一体化的内部环境。 将其私有 `properties.staticIp` 传给每个 silo。
- 至少两个,通常三个,一个复制的 silo 程序,用于处理和应用程序冗余。每个 silo 程序都有 `minReplicas: 1` 和 `maxReplicas: 1`;这并不能提供有文件记录的跨区安插保证。
- 每个应用程序都有单独的 silo 和网关端口对, 映射到集装箱监听端口。 仅在内部环境中启用应用程序级外部 TCP 入侵 。
- 显性启动、准备和活性探测
- A `terminationGracePeriodSeconds` 大于所测量的数值 .NET 和主机 Orleans 容器 Apps 将流程宽度默认为30秒,并发送 `SIGKILL` 其到期时。
- 每个运行时间应用程序都有一个用户指定或系统指定的管理身份。
- [管理身份图像拉动](https://learn.microsoft.com/azure/container-apps/managed-identity-image-pull) 调自 Azure 集装箱登记处,仅赠款 `AcrPull`,或使用ABAC的登记处在存储处-阅读器方面的相应作用。
- Azure 表格存储或另一支助的集群提供商、必要时耐用 grain 存储以及安全模式所要求的私人服务准入。
- 日志分析或 Azure 监测诊断路径,加上应用测量和跟踪。
- 不易变图像摘要或独特的图像标记。 不要使用可变 `latest` 标记 。

永远不要将 Orleans 组块缩放为零。 保持足够 silo 应用程序在丢失一个实例后运行以满足测试的可用性和能力底线。 缩小规模, 添加一个新的一版 silo 应用程序并等待它成为会员。 在从应用程序流量中删除该程序并允许宽度关闭之后, 一次调整一个应用程序 。 见 [设定 Azure 容器应用程序中的缩放规则](https://learn.microsoft.com/azure/container-apps/scale-app)。

真正秘密更喜欢集装箱装货包 [密钥“空洞秘密查询”](https://learn.microsoft.com/azure/container-apps/manage-secrets#reference-secret-from-key-vault) 使用管理下身份 **密钥“密码”用户**。不要直接将存储钥匙、登记密码、证书或部署证明直接置于源头、图像、比塞普参数或普通环境变量中。

<a id="secure-continuous-deployment"></a>

## 安全连续部署

使用 [GitHub OpenID 连接(OIDC)和工作量身份联合会](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect) ,而不是长期服务主要秘密:

```yaml
permissions:
  contents: read
  id-token: write

steps:
  - uses: azure/login@f5d393ae46f8fde4be8b75f32e3fc50e654ad0ca # v3.0.1
    with:
      client-id: ${{ vars.AZURE_CLIENT_ID }}
      tenant-id: ${{ vars.AZURE_TENANT_ID }}
      subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
```

将联合认证限制在部署的储存处和受保护分支或 GitHub 环境。

负责者分别识别和允许:

- 例行部署身份可以更新预定的集装箱应用程序和相关资源,并由一个资源组进行更新。
- 图像出版身份只在目标存储库中具有 `AcrPush`或存储器范围作家作用。
- 运行时集装箱应用程序仅使用图像拉动和应用数据机许可,而使用不同身份。
- 单独管理的“陷阱”身份可产生角色分配。 不要允许在用户 **贡献者** 或允许指定任意角色。

在使用它作为 grain 键。可以选择任意键的攻击者可强行将数个 grain 启动。 绕过被接受的关键空间,授权进入,限制费率请求,并避免原始记录 grain 键作为度量尺寸。

<a id="configure-health-and-shutdown"></a>

## 配置健康和关闭

将 HTTP 启动、准备状态和活性探测器明确定义为基础设施中的代码。 集装箱应用程序支持每个容器每类一次的HTTP(S)或TCP探测; 它不支持 `exec` 或 gRPC 探测。

- **启动** 成功,在配置有效、听众受约束和 silo 加入预定组群后。
- **准备状态** 只有在应用程序能够安全接受新应用程序流量的情况下才能成功。 当关闭开始时, 让它失效 。
- **生命** 检查本地进程进度。 不要让它依赖于 Azure 储存,另一个 silo,或另一种远程服务,因为共享断电可以重新启动整个组群。

集装箱应用系统反向流量。 silo 应用程序, 无法就绪, 通过广告防止新连接 silo 和入口的入侵端口,但现有的 TCP 连接可以保持开放。 准备状态不更新 Orleans 协调准备状态过渡,停止新的申请工作、退出成员资格、关闭现有联系和正常 .NET 主机终止。 设置 `terminationGracePeriodSeconds` 超过所测停产时间,并有一定的差额,并保持 .NET 运行关闭超时时间比平台的最后期限短 。

见 [Azure 容器内的健康探测器](https://learn.microsoft.com/azure/container-apps/health-probes)、 [Azure 集装箱应用生命周期](https://learn.microsoft.com/azure/container-apps/application-lifecycle-management)和 [健康和可观察性](health-and-observability.md)。

<a id="plan-revisions-and-upgrades"></a>

## 计划修订和升级

容器应用软件默认使用单一版本模式。 它可以在等待启动和准备时临时运行旧的和新的复制品。 如果两个版本都有一个广告的应用程序端点, 重复功能对一个复制件 silo 应用程序不安全 。

故意替换 silo 应用程序:

1. 安装一个替换应用程序,配有新一对暴露港口,并在版本兼容时使用相同的组群特性。
1. 等待替换者准备好并积极担任 Orleans 成员。
1. 停止旧的 silo应用程序工作,允许优雅地关闭,并确认退出成员资格。
1. 仅在集成容量和延迟稳定后删除旧应用程序。

这一过程是跨越不同的集装箱应用程序资源,而不是交通量加权修订推出的滚动替换。

旧和新 silos 能够共享 `ClusterId` 只有当 grain 相容的蓝绿色部署使用 `ClusterId` 和终端点设置。 HTTP 路径应用程序在匹配客户端或 API 集装箱应用软件修改权重和标签仅影响入侵交通;它们不会直接分割 Orleans 交通流量TCP。

见 [Azure 集装箱应用程序中的修订](https://learn.microsoft.com/azure/container-apps/revisions)、 [分道通交通](https://learn.microsoft.com/azure/container-apps/traffic-splitting)和 [优优优停机和升级](upgrades.md)。

<a id="configure-observability"></a>

## 配置可观测性

出口应用日志、 Orleans 和 .NET 指标,并集中分布跟踪。

- `CONTAINER_APP_NAME`、 `CONTAINER_APP_REVISION`和 `CONTAINER_APP_REPLICA_NAME`。
- Orleans silo 名称、广告告示的终点、 `ServiceId`和 `ClusterId`。
- 图像摘要或部署版本。

随时和积极监测 silo 计数,会籍变化,网关连接 grain 调用延迟和故障、 激活计数、 CPU、 内存、 套接、 套接、 重新启动以及供应商的延时或抽动。 容器应用程序控制台和系统日志包括应用、 修改和复制元数据。 启用 HTTP 日志用于公开应用反向, 不要发布秘密、 租户数据或者未登录 grain 键。

<a id="validate-the-deployment"></a>

## 核查部署情况

准确的集装箱应用环境及网络配置的确定性证据记录下来。

```azurecli
az containerapp revision list \
  --resource-group "$RESOURCE_GROUP" \
  --name "$CONTAINER_APP" \
  --query "[].{name:name,active:properties.active,replicas:properties.replicas,health:properties.healthState,running:properties.runningState}" \
  --output table

az containerapp replica list \
  --resource-group "$RESOURCE_GROUP" \
  --name "$CONTAINER_APP" \
  --revision "$REVISION" \
  --output json
```

复制 API 不返回支持的应用程序网络IP地址。 请使用 `az containerapp exec` 检查每个复制的环境和监听套接字, 但不要从所观察到的接口地址推断支持保证 。

查询 Azure 使用 Microsoft 内容认证 :

```azurecli
az storage entity query \
  --account-name "$STORAGE_ACCOUNT" \
  --auth-mode login \
  --table-name OrleansSiloInstances \
  --filter "PartitionKey eq '$CLUSTER_ID'" \
  --output table
```

生产前和生产后及平台或网络变化之后完成这些验收测试:

1. 确认每个预定 silo 有一个现任成员行,有一个独特的广告 silo 和网关端点。
1. 从每个 silo 应用程序打开一个连接到其他每一个已公布 silo 终点的 TCP 连接。
1. 从每个客户网络,打开一个TCP连接到每一个广告的网关端点。
1. 通过一组封闭的测试 grain 键发送呼叫,并核实该呼叫在集群内达到启动。
1. 添加替换 silo,删除一个旧的 silo ,确认会籍变化优雅、稳定状态和可接受的延迟。
1. 依次重新开始每个 silo ,并核实耐久 grain 状态是否得以生存。
1. 进行兼容的滚动和孤立的蓝色绿色程序,包括回落。
1. 验证 silo 和网关港口无法公开进入,运行时间和部署身份仅具有预期作用。

<a id="understand-the-sample"></a>

## 理解样本

下 [内回移样本](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps),从 [原始 Azure 样本](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps),表明:

- 两名专 silo 集装箱自动装箱、仪表板 silo,一个最小 API 客户、工人客户和外部服务在一个内部环境中进行。
- 复制一次 Orleans 服务器应用程序。 两者 silos 以及使用独特的对口港口广告, `11111`/`30000`, `11112`/`30001`,和 `11113`/`30002`.
- 稳定集装箱应用资源API、与私营DNS的虚拟网络整合、明确的启动/准备/寿命探测、60秒终止宽限期和非零复制层。
- 用户指定的运行时间身份、管理下身份识别码拉动、残疾登记册管理员证书和残疾存储共享钥匙访问。
- Azure 表格存储群集通过 <xref:Azure.Identity.DefaultAzureCredential>。运行时身份在预创建的成员表上含有 **存储表数据贡献器** 。样本不配置耐久的 grain 储存。
- 角色分配的单独运行的靴套模板和常规部署工作流程,使用 GitHub OIDC、SHA型动作、Git-SHA图像标签以及摘要集装箱应用程序修订。
- 客户通过 Orleans 成员身份发现个别网关,而不是使用HTTP入侵作为 Orleans 运输工具。
- 外部缩放器 gRPC 用作学习内容的服务。它不附于 silo 缩放规则,因为将一个应用程序缩放到多个 silo 复制将重新采用不支持的终点假设。

抽样范围公开 grain 身份,它的喜好 grain 使用整数键,公共路径在拨打前只接受0至255的密钥 Orleans,提供者端点返回数字键,不活动喂 grains 保存在修改时的约束式输入模式 API。单靠收集年龄并不能保证无限制的关键空间的安全。

样本仍然是建筑演示, 而不是生产部署的标记。 注册和存储数据端点仍然公开, 尽管它们需要 Microsoft Entra 认证。 所有运行时间应用程序都有一个特性。 简单的准备端点不执行特定应用程序的排水, 工作流程更新现有的应用程序, 这些应用程序可以暂时重叠一些修改, 以广告同样的终点。 对于产品升级, 请使用本页的替换应用程序。 一号应用程序并不证明跨区或独立的故障域定位, 而外部缩放器没有给出能力建议 。

使用样本来理解组件关系, Orleans 以成员为基础的网关发现、管理身份、工作量生成和明确的终点映射。 在使用生产中的地形图之前,完成本页的网络建设、失败、升级、安全、州恢复和准备状态接受测试。

<a id="production-checklist"></a>

## 生产清单

| 关注问题 | Azure 集装箱集装箱 |
| --- | --- |
| 地形学和联网 | 每个复制 silo 应用程序都拥有一个独特的专用的私有 silo 和网关端口对,每个成员端点地图都精确地显示一个应用程序。 |
| 依赖和数据 | 生产集群、 grain 储存、提醒和流有明确的供应商、命名空间、持久性、配额、备份和回收程序。 |
| 身份和机密 | 运行时间、图像出版、常规部署和特权靴子身份具有各自不同的最不重要的角色。 秘密使用管理下的交付和轮换。 |
| 健康和生命周期 | 启动、准备和活性探测代表不同的应用状态,终止宽限期超过测算的主机停机时间。 |
| 规模和复原力 | 缩放变化增加或删除一个复制的 silo 应用程序,保留测试能力下限,并成功从一个应用或主机损失中恢复。 |
| 升级和回调 | 兼容版本使用新端口对的替换应用程序。 不兼容版本使用单独的集束识别码、终端设置、州计划和应用-交通切换。 |
| 观察和事件 | Orleans 遥测、集装箱应用应用程序/审校/复制身份、成员身份、供应商信号和部署元数据是核心关联的。 |
| 基础设施交付 | 版本的 Bicep 和受保护 GitHub OIDC工作流程复制了封闭式应用程序表层学,并采用了不可变图像摘要。 |

将目标与 [AKS、App Service、 Kubernetes、服务制造和多主集装箱](choose-deployment-target.md)比较,然后完成共享 [生产准备状态清单](production-readiness.md)。
