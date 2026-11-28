---
title: 在 Azure Container Apps 上托管 Orleans
description: 在 Azure Container Apps 上设计、保护、部署并验证 Orleans 集群。
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="host-orleans-on-azure-container-apps"></a>

# 在 Azure Container Apps 上托管 Orleans

Azure Container Apps 使用能够保留 Orleans 终结点语义的拓扑来托管 Orleans。Orleans Silo 连接到各成员通告的唯一 Silo 和网关终结点。Container Apps 的服务发现及 TCP 或 HTTP 入口面向**容器应用或修订版**，并在其副本之间进行负载均衡。因此，当前维护的生产部署拓扑为每个 Silo 分配独立的 Container App，且每个应用只有一个副本。

使用已集成虚拟网络的内部环境及其专用静态 IP。将每个 Silo 部署为独立的 Container App，且恰好运行一个副本，为每个应用分配该专用 IP 上唯一的一对 TCP 入口端口。通过重复创建此类资源来增加 Silo，然后完成本页的生产验收测试。一个 Container App 中运行多个 Silo 副本不属于当前维护的部署模式，因为 Container Apps 发布的是应用和修订版终结点，而不是受支持的逐副本终结点标识。

参阅[在 Azure 上部署和缩放 Orleans 应用](../quickstarts/deploy-scale-orleans-on-azure.md) 快速入门，了解 Azure Developer CLI 部署流程。[Azure Container Apps 上的 Orleans 集群示例](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps) 也有助于研究多组件拓扑和外部缩放器契约。在将任一资源改造用于生产环境之前，请查看[示例的限制](#understand-the-sample)。

如果应用程序使用 Aspire，[适用于 Azure Container Apps 的 Aspire 部署流程](https://aspire.dev/deployment/azure/container-apps/) 可以预配及发布平台资源。但它不会消除本页所述的 Orleans 终结点要求：检查生成的拓扑，确保每个已通告的 Silo 终结点仅对应一个副本，配置外部集群提供程序和持久化状态，并在投产之前添加所需的健康检查、标识、升级及容量控制措施。

<a id="choose-a-topology"></a>

## 选择拓扑

| 拓扑 | 终结点行为 | 指南 |
| --- | --- | --- |
| 每个 Container App 一个 Silo 副本 | 环境专用静态 IP 上唯一的一对 Silo 和网关端口路由到一个应用和一个 Silo。 | 必须使用 Container Apps 时采用此拓扑。将最小和最大副本数均设为 1，并部署多个 Silo 应用。 |
| 一个 Container App 中有多个 Silo 副本 | 应用及修订版主机名通过平台代理路由到任意符合条件的副本。Container Apps 不发布用于选定单个副本的受支持地址。 | 使用当前维护的每应用单副本拓扑，或选择 AKS 等提供直接寻址能力的平台。 |
| Kubernetes 或其他支持直接寻址的平台上的 Silo | 编排器为每个 Pod 或进程发布可路由地址。 | 当需要自动缩放副本和常规滚动替换时，优先采用此拓扑。 |

每应用单副本拓扑的运维成本更高，因为基础设施即代码必须创建数量有界的 Silo 应用集合，并分配在环境内唯一的端口。升级时还需要经过规划的替换流程。其优势是不依赖未记录的 Pod 网络行为。

多个单副本应用可以提供进程及应用资源级冗余，但 Container Apps 并未明确说明独立应用之间的反亲和性或跨可用区放置保证。其可用区冗余指南将同一应用的多个副本分散部署，并要求至少两个副本，这与本终结点模型冲突。不要声称每应用单副本拓扑具备可用区冗余。如果需要独立故障域放置，请使用同时提供逐进程直接地址及放置控制的平台。

将公共 HTTP API 放在独立的、托管 Orleans 客户端的 Container App 中；仅在明确选择该拓扑时，才让每个 Silo 同时托管 HTTP API。公共 HTTP 入口路由应用程序请求，它不是 Orleans 传输通道。

<a id="configure-networking-and-orleans-endpoints"></a>

## 配置网络和 Orleans 终结点

在现有虚拟网络中创建 Container Apps 环境，将可访问性级别设置为 **Internal**（内部）。这样，环境便具有 Azure 内部负载均衡器上的专用静态 IP，而没有公共终结点。

为每个 Silo 应用配置两个 TCP 入口端口：

- **Silo 端口**承载 Silo 之间的流量。
- **网关端口**承载来自 Orleans 客户端的流量。

将应用入口设为 `external: true`，使这些端口可在环境边界访问。由于环境本身是内部环境，入站 IP 仍是虚拟网络中的专用 IP。为每个 Silo 应用分配唯一的公开 Silo 端口和网关端口。对外公开的 TCP 端口必须在整个环境内唯一；端口 `36985` 为保留端口。每个应用可以使用相同的容器目标端口，例如 `11111` 和 `30000`。

将客户端放在同一环境，或能够访问该环境专用 IP 的已连接专用网络中。将网络访问限制为受信任的工作负载；当网络不属于可信边界时，使用 [Orleans 传输层安全性](../host/transport-layer-security.md)。

向每个 Silo 传递环境的 `properties.staticIp` 以及该应用唯一的公开端口。将监听终结点绑定到容器目标端口：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_endpoint_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_endpoints":::

**监听终结点**是进程在其容器内绑定的位置。**通告终结点**是 Orleans 写入成员资格信息、供对等 Silo 和客户端使用的地址。绑定到 `0.0.0.0` 并不会发现需要通告的地址。如果入口映射的公开端口与目标端口不同，请直接配置 <xref:Orleans.Configuration.EndpointOptions>，使得：

- <xref:Orleans.Configuration.EndpointOptions.SiloListeningEndpoint> 和 <xref:Orleans.Configuration.EndpointOptions.GatewayListeningEndpoint> 包含目标端口。
- <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress>、<xref:Orleans.Configuration.EndpointOptions.SiloPort> 和 <xref:Orleans.Configuration.EndpointOptions.GatewayPort> 包含可路由的入口地址及公开端口。

Container Apps 提供 `CONTAINER_APP_REPLICA_NAME` 来标识副本，并提供 `CONTAINER_APP_HOSTNAME` 来标识修订版主机。两者都不是受支持的逐副本网络地址。不要从多个 Silo 副本通告同一应用或修订版主机名：对等 Silo 可能被路由到与成员资格条目标识的副本不同的副本。

有关底层网络要求，请参阅 [Azure Container Apps 中的网络](https://learn.microsoft.com/azure/container-apps/networking)、[配置入口](https://learn.microsoft.com/azure/container-apps/ingress-how-to)、[在多个主机的容器中运行 Orleans](containers.md) 及[拓扑、网络与集群](networking.md)。

<a id="configure-clustering-and-durable-state"></a>

## 配置集群与持久化状态

使用外部集群提供程序，使 Silo 和客户端发现相同的成员资格记录。Azure Table Storage 是常见的选择。集群提供程序存储成员资格信息，但不持久化 Grain 状态。对于每个需要在激活丢失或集群丢失后保留的状态名称，单独注册 Grain 存储提供程序。

以下配置使用 Microsoft Entra ID，而不是存储帐户密钥：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_apps_storage_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_apps_storage":::

此配置需要安装 `Microsoft.Orleans.Clustering.AzureStorage`、`Microsoft.Orleans.Persistence.AzureStorage` 和 `Azure.Identity`。在 Orleans 客户端上配置相同的 `ServiceId`、`ClusterId`、集群表和凭据。保持应用程序的 `ServiceId` 稳定，并为每个环境或隔离的蓝绿集群使用不同的 `ClusterId`。

仅在 Silo 和客户端标识使用的表或存储帐户上授予 **Storage Table Data Contributor** 角色。Orleans Azure Table 提供程序会调用 <xref:Azure.Data.Tables.TableClient.CreateIfNotExistsAsync*?displayProperty=nameWithType>，因此即使客户端随后仅读取网关记录，单独授予 **Storage Table Data Reader** 也不够。如果 Grain 状态使用 Blob 或其他服务，则在可行的最小范围授予对应的数据平面角色。当所有使用方都支持 Microsoft Entra ID 时，禁用共享密钥访问。

<a id="provision-production-infrastructure"></a>

## 预配生产基础设施

使用稳定版 Azure Resource Manager API 定义环境及每个 Container App。生产模板应包含：

- 已集成虚拟网络的内部环境。将其专用 `properties.staticIp` 传递给每个 Silo。
- 至少两个、通常三个单副本 Silo 应用，以提供进程及应用资源级冗余。每个 Silo 应用均设置 `minReplicas: 1` 和 `maxReplicas: 1`；这并不提供文档明确保证的跨可用区放置能力。
- 每个应用具有不同的一对公开 Silo 和网关端口，映射到容器监听端口。仅在内部环境中启用应用级外部 TCP 入口。
- 显式配置的启动、就绪和存活探针。
- 一个大于实测 .NET 主机和 Orleans 关闭耗时的 `terminationGracePeriodSeconds` 值。Container Apps 的默认进程终止宽限期为 30 秒，到期时发送 `SIGKILL`。
- 每个运行时应用各自使用用户分配或系统分配的托管标识。
- 从 Azure Container Registry [使用托管标识拉取映像](https://learn.microsoft.com/azure/container-apps/managed-identity-image-pull)。仅授予 `AcrPull`，或在使用 ABAC 的注册表上授予等效的存储库读取者角色。
- Azure Table Storage 或其他受支持的集群提供程序、需要时使用的持久化 Grain 存储，以及安全模型要求的专用服务访问。
- Log Analytics 或 Azure Monitor 诊断路由，以及应用程序指标和跟踪。
- 不可变映像摘要或唯一映像标记。不要部署可变的 `latest` 标记。

切勿将 Orleans 集群缩容到零。保持足够的 Silo 应用运行，以便在丢失一个实例后仍满足经过测试的可用性和容量下限。扩容时添加一个新的单副本 Silo 应用，并等待其在成员资格中变为活动状态。缩容时每次只移除一个应用，先将其从应用程序流量中移除，再允许它优雅关闭。请参阅[在 Azure Container Apps 中设置缩放规则](https://learn.microsoft.com/azure/container-apps/scale-app)。

对于真正的机密，优先使用 Container Apps 的 [Key Vault 机密引用](https://learn.microsoft.com/azure/container-apps/manage-secrets#reference-secret-from-key-vault)，并使用具有 **Key Vault Secrets User** 角色的托管标识。不要将存储密钥、注册表密码、证书或部署凭据直接放在源码、映像、Bicep 参数或普通环境变量中。

<a id="secure-continuous-deployment"></a>

## 保护持续部署

使用 [GitHub OpenID Connect (OIDC) 和工作负载标识联合身份验证](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect)，而不是长期有效的服务主体机密：

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

将联合凭据限制到实际执行部署的存储库及受保护分支或 GitHub 环境。将第三方 Action 固定到经过审查的提交 SHA。

按职责划分标识与权限：

- 常规部署标识可以在一个资源组内更新指定的 Container Apps 及相关资源。
- 映像发布标识仅在目标存储库上拥有 `AcrPush`，或存储库范围的写入者角色。
- 运行时 Container Apps 使用另一个标识，该标识仅拥有映像拉取及应用程序数据平面权限。
- 由独立治理的引导标识创建角色分配。不要为常规工作流授予订阅范围的 **Contributor** 角色或分配任意角色的权限。

在将不受信任的输入用作 Grain 键之前进行验证。能够选择任意键的攻击者可以强制创建数量无界的 Grain 激活。限制允许的键空间，对访问进行授权，限制请求速率，并避免将原始 Grain 键记录为指标维度。

<a id="configure-health-and-shutdown"></a>

## 配置健康检查和关闭

在基础设施即代码中显式定义 HTTP 启动、就绪和存活探针。Container Apps 对每个容器的每种探针类型支持一个 HTTP(S) 或 TCP 探针，但不支持 `exec` 或 gRPC 探针。

- **启动检查**在配置有效、监听器已绑定且 Silo 已加入目标集群之后成功。
- **就绪检查**仅在应用程序能够安全接受新应用流量时成功。在开始关闭时使其失败。
- **存活检查**检查本地进程的运行进展。不要使其依赖 Azure Storage、其他 Silo 或其他远程服务，因为共享依赖项的中断可能导致整个集群重启。

就绪状态控制 Container Apps 的入口流量。对于单副本 Silo 应用，就绪检查失败会阻止新的连接通过其已通告的 Silo 和网关入口端口，但现有 TCP 连接可能保持打开。就绪状态本身不会更新 Orleans 成员资格或完成排空。将就绪状态转换与停止新的应用程序工作、退出成员资格、关闭现有连接及 .NET 主机正常终止协调起来。将 `terminationGracePeriodSeconds` 设置为高于实测关闭耗时且留有余量，并保持 .NET 主机关闭超时短于该平台截止时间。

请参阅 [Azure Container Apps 中的健康探针](https://learn.microsoft.com/azure/container-apps/health-probes)、[Azure Container Apps 中的应用程序生命周期](https://learn.microsoft.com/azure/container-apps/application-lifecycle-management) 和[健康检查与可观测性](health-and-observability.md)。

<a id="plan-revisions-and-upgrades"></a>

## 规划修订版与升级

Container Apps 默认使用单修订版模式。在等待启动和就绪的过程中，它可能暂时同时运行新旧副本。如果单副本 Silo 应用的两个修订版共用一个已通告的应用终结点，这种重叠就是不安全的。

按明确的步骤替换 Silo 应用：

1. 如果版本兼容，部署一个具有新的一对公开端口且集群标识相同的替代应用。
1. 等待替代应用就绪，并在 Orleans 成员资格中处于活动状态。
1. 停止旧 Silo 上的应用程序工作，允许其优雅关闭，并确认其退出成员资格。
1. 仅在集群容量和延迟趋于稳定后移除旧应用。

此过程是在不同 Container App 资源之间进行滚动替换，而不是基于流量权重的修订版发布。

只有当新旧 Silo 的 Grain 接口、序列化器、持久化状态、提供程序架构和副作用均兼容时，它们才能共用一个 `ClusterId`。对于不兼容的蓝绿部署，使用不同的 `ClusterId` 和终结点集合。在匹配的客户端或 API 部署之间路由应用程序 HTTP 流量。Container Apps 修订版权重和标签只影响入口流量，不会拆分 Orleans 的直接 TCP 流量。

请参阅 [Azure Container Apps 中的修订版](https://learn.microsoft.com/azure/container-apps/revisions)、[流量拆分](https://learn.microsoft.com/azure/container-apps/traffic-splitting) 和[优雅关闭与升级](upgrades.md)。

<a id="configure-observability"></a>

## 配置可观测性

集中导出应用程序日志、Orleans 和 .NET 指标及分布式跟踪。在日志和跟踪中添加以下维度：

- `CONTAINER_APP_NAME`、`CONTAINER_APP_REVISION` 和 `CONTAINER_APP_REPLICA_NAME`。
- Orleans Silo 名称、通告终结点、`ServiceId` 和 `ClusterId`。
- 映像摘要或部署版本。

监控就绪及活动 Silo 数量、成员资格变更、网关连接、Grain 调用延迟及失败、激活数量、CPU、内存、套接字使用量、重启以及提供程序延迟或限流。Container Apps 控制台及系统日志包含应用、修订版和副本元数据。为公共应用程序入口启用 HTTP 日志，并且不要输出机密、租户数据或无界的 Grain 键。

<a id="validate-the-deployment"></a>

## 验证部署

针对确切的 Container Apps 环境及网络配置，记录可确定验证结果的证据。首先枚举活动修订版和副本：

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

副本 API 不会返回受支持的应用程序网络 IP 地址。使用 `az containerapp exec` 检查每个副本的环境及监听套接字，但不要根据观察到的接口地址推断平台提供了支持保证。

使用 Microsoft Entra 身份验证查询 Azure Table 成员资格：

```azurecli
az storage entity query \
  --account-name "$STORAGE_ACCOUNT" \
  --auth-mode login \
  --table-name OrleansSiloInstances \
  --filter "PartitionKey eq '$CLUSTER_ID'" \
  --output table
```

在投产之前及平台或网络变更之后，完成以下验收测试：

1. 确认每个预期 Silo 都有一条活动成员资格记录，且其中通告的 Silo 和网关终结点均唯一。
1. 从每个 Silo 应用建立到其他所有已通告 Silo 终结点的 TCP 连接。
1. 从每个客户端网络建立到所有已通告网关终结点的 TCP 连接。
1. 使用有界的测试 Grain 键集合发送调用，验证调用能够到达整个集群中的激活。
1. 添加一个替代 Silo，在负载下移除一个旧 Silo，并确认成员资格优雅变更、状态稳定以及延迟可接受。
1. 依次重启每个 Silo，验证持久化 Grain 状态仍然存在。
1. 演练兼容的滚动部署及隔离的蓝绿部署流程，包括回滚。
1. 验证 Silo 和网关端口无法从公网访问，且运行时标识和部署标识仅拥有预期角色。

<a id="understand-the-sample"></a>

## 了解示例

[仓库内的示例](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps) 从[原始 Azure 示例](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps) 导入，演示了：

- 一个内部环境中的两个专用 Silo Container App、一个仪表板 Silo、一个 Minimal API 客户端、一个工作进程客户端及一个外部缩放器服务。
- 每个 Orleans 服务器应用一个副本。两个 Silo 和仪表板通告环境的专用静态 IP，并使用唯一的公开端口对：`11111`/`30000`、`11112`/`30001` 和 `11113`/`30002`。
- 稳定版 Container Apps 资源 API、具有专用 DNS 的虚拟网络集成、显式启动/就绪/存活探针、60 秒终止宽限期及非零副本数下限。
- 用户分配的运行时标识、通过托管标识从 ACR 拉取映像、禁用注册表管理员凭据，以及禁用存储共享密钥访问。
- 通过 <xref:Azure.Identity.DefaultAzureCredential> 使用 Azure Table Storage 集群提供程序。运行时标识在预创建的成员资格表上拥有 **Storage Table Data Contributor** 角色。该示例未配置持久化 Grain 存储。
- 用于角色分配的独立运行的引导模板，以及使用 GitHub OIDC、固定到 SHA 的 Action、Git SHA 映像标记和固定到摘要的 Container App 修订版的常规部署工作流。
- 客户端通过 Orleans 成员资格发现各个网关，而不是使用 HTTP 入口作为 Orleans 传输通道。
- 作为学习组件的外部缩放器 gRPC 服务。它并未关联到 Silo 缩放规则，因为将一个应用扩展到多个 Silo 副本会重新引入不受支持的终结点假设。

示例限制了对外公开的 Grain 标识范围。其 hello Grain 使用整数键，公共路由仅接受 0 到 255 的键之后才调用 Orleans，提供程序终结点返回数字键，非活动 hello Grain 的空闲回收期限设置为两分钟。在改造 API 时保留这种有界输入模式。仅靠回收时间无法保证无界键空间的安全性。

该示例仍是架构演示，而不是生产部署清单。注册表和存储数据终结点仍然是公开的，尽管它们要求 Microsoft Entra 身份验证。所有运行时应用共享同一个标识。简单的就绪终结点未实现应用程序特定的排空流程，而且工作流会就地更新现有应用，这可能导致通告同一终结点的修订版暂时重叠。生产升级应使用本页的替代应用流程。单副本应用不能证明具备跨可用区或独立故障域放置能力，外部缩放器也不提供容量建议。

使用该示例了解组件关系、基于 Orleans 成员资格的网关发现、托管标识、工作负载生成及显式终结点映射。在生产环境使用此拓扑之前，完成本页的网络、故障、升级、安全、状态恢复和就绪验收测试。

<a id="production-checklist"></a>

## 生产检查清单

| 关注事项 | Azure Container Apps 的预期结果 |
| --- | --- |
| 拓扑和网络 | 每个单副本 Silo 应用拥有唯一的一对专用 Silo 和网关端口，每个成员资格终结点恰好映射到一个应用。 |
| 依赖项和数据 | 生产集群、Grain 存储、提醒器及流均具有明确的提供程序、命名空间、持久性、配额、备份及恢复流程。 |
| 标识和机密 | 运行时、映像发布、常规部署和特权引导标识具有相互独立的最小权限角色。机密采用托管交付及轮换。 |
| 健康和生命周期 | 启动、就绪及存活探针分别代表不同的应用程序状态，终止宽限期大于实测主机关闭耗时。 |
| 缩放和韧性 | 缩放变更通过增加或移除单副本 Silo 应用实现，维持经过测试的容量下限，并能够从单个应用或主机丢失中成功恢复。 |
| 升级和回滚 | 兼容版本使用新的端口对部署替代应用。不兼容版本使用隔离的集群 ID、终结点集合、状态规划及应用程序流量切换。 |
| 可观测性和事件处置 | 集中关联 Orleans 遥测、Container App 的应用/修订版/副本标识、成员资格、提供程序信号及部署元数据。 |
| 基础设施交付 | 版本化的 Bicep 和受保护的 GitHub OIDC 工作流能够复现有界应用拓扑，并部署不可变映像摘要。 |

将此部署目标与 [AKS、App Service、Kubernetes、Service Fabric 和多主机容器](choose-deployment-target.md) 进行比较，然后完成通用的[生产就绪检查清单](production-readiness.md)。
