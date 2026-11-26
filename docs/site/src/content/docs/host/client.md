---
title: "Orleans 客户"
description: "选择并主持 Orleans 客户端。"
ms.date: 08/18/2026
ms.topic: concept-article
---

<a id="orleans-clients"></a>

# Orleans 客户

Orleans 客户端允许应用程序代码调用 grains,使用流并访问其他集服务。 Orleans 在每个 silo内自动提供客户端,或者您可以在另一个程序中托管外部客户端。

<a id="choose-a-hosting-model"></a>

## 选择主机模式

| 型 型 | 更喜欢当 | 权衡平衡 |
|---|---|---|
| 共同主持客户 | HTTP终点点、背景工人和 grains 可分享一个进程。 | 简单地形学和最快的电话,但客户工作量分担 silo CPU和记忆。 |
| 外部客户 | 前端和 silos 需要独立的规模、部署、安保或资源隔离。 | 添加网关、网络跳和另一个运行程序。 |

除非隔离是要求,否则从共同托管客户开始。

<a id="co-hosted-clients"></a>

## 共同托管客户

<a id="obtain-a-client-from-a-host"></a>

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 在东道服务供应商中登记 <xref:Orleans.IClusterClient> 和 <xref:Orleans.IGrainFactory> :

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="local_silo_and_client":::

来自联合托管客户端的电话使用 silo集知识, 不需要网关。 如果目标激活是本地的, Orleans 也可以避免网络跳动 。

<a id="call-grains-from-aspnet-core"></a>

### 调调 grains 调自 ASP.NET 核心核心核心核心核心

`orleans-web` 模板创建了一个 ASP.NET 核心应用程序,该程序将共同主办一个 silo ,并通过 HTTP 端点显示一个 grain :

```dotnetcli
dotnet new install Microsoft.Orleans.Templates
dotnet new orleans-web --name HelloOrleans --output HelloOrleans
dotnet run --project HelloOrleans/HelloOrleans.csproj
```

从另一个终端调用终点 :

```console
curl http://localhost:5000/hello/Ada
```

终点回归 `Hello, Ada!`. ASP.NET 核心决心 <xref:Orleans.IGrainFactory> 注射后,终点从依赖注射中取得 grain 键用 `Ada`,和 Orleans 激活 grain 当请求书提出时。

生成的应用程序将本地主机群集用于单节开发组。 对于多silo 部署,配置 [共享集群和存储供应商](configuration-guide/typical-configurations.md) 并应用 [生产准备状态清单](../deployment/production-readiness.md)。

<a id="external-clients"></a>

## 外部客户

<a id="initialize-a-grain-client"></a>
<a id="example"></a>

安装 [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client),然后将客户端添加到 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 和 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>:

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="external_client":::

客户端在主机启动时连接,可从 [.NET 依赖性注射](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) 之后,登记使用 Orleans 之后 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 所以主机在客户后启动它们 :

:::code language="csharp" source="snippets/ClusterClientHostedService.cs" id="cluster_client_hosted_service":::

组合和网关设置见 [客户配置](configuration-guide/client-configuration.md) 。

<a id="client-connectivity"></a>

## 客户连连

Orleans 包含默认连接再试过滤器。 在初始启动期间, 它会重新输入线性回溯( 最多15次) 中符合条件的连接失败。 如果这些重试已用尽, 主机无法启动而不是显示没有集连接的健康状态 。

您可以用 <xref:Orleans.Hosting.ClientBuilderExtensions.UseConnectionRetryFilter*> 或 <xref:Orleans.IClientConnectionRetryFilter>替换默认值。 自定义策略应该:

- 只重试瞬态失败 。
- 适用有限的尝试或时限。
- 荣誉号提供取消代币
- 当组群或配置一直无法使用时, 启动失败 。

启动取消中断了网关发现、连接尝试和初始集控检索。 客户停止初始化工作, 在取消的启动完成之前释放部分启动客户资源。 启动成功后, 网关和显示更新会跟随客户的一生进行 。

自定义网关提供者执行 <xref:Orleans.Messaging.IGatewayListProvider.InitializeGatewayListProviderAsync*> 和 <xref:Orleans.Messaging.IGatewayListProvider.GetGatewaysAsync*> ,并将提供的标语传递给取消的、知道的 I/O 操作。对于实施无一例外方法的供应商,默认接口执行会取消呼叫者的等待并观察提供者操作的最终完成情况。

<a id="client-lifetime-and-registration"></a>

### 客户使用寿命和登记

对于通用主机和 ASP.NET 核心应用程序,在申请过程的整个期间将 <xref:Orleans.IClusterClient> 作为单吨处理。将其登记一次,用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 解决依赖注射问题,而不是根据请求、按控制器或静态字段创建第二个客户端。

网络应用程序或工人可以安全地在所有请求和背景服务中共享同一个客户,因为 Orleans 设计时同时使用多个线条。 客户对再利用来说是线索安全的; 保护的只有可变应用程序说明您在 Orleans之外共享。

让主机客户端启动和关闭。 主机在应用程序启动时启动客户端, 并在正常终止期间关闭客户端, 所以您不应该手动处理自上输入的单键。 如果在启动时无法使用该组群, 主机会快速失败, 而不是将应用程序留在部分启动状态 。

启动后, Orleans 将更新网关,并随着分组成员的变化重新连接。如果网关或 silo 无法使用,客户端会尝试自动重新连接,而个人 grain 呼叫仍可显示短暂故障。 grain 引用仍然有效,但只有在应用程序能够安全地容忍重复执行的情况下才能重试操作。

<a id="make-calls-to-grains"></a>

## 拨打 grains

外部客户端代码不受 grain 转折货币模式的制约。多个线索可以同时使用 <xref:Orleans.IClusterClient> 和 grain 引用。使用正常 .NET 同步来保护可变客户端状态 。

Grain 返回电话 <xref:System.Threading.Tasks.Task>, <xref:System.Threading.Tasks.Task`1>, <xref:System.Threading.Tasks.ValueTask>,或 <xref:System.Threading.Tasks.ValueTask`1> 依时间 [grain 接口规则](../grains/index.md)总是等待电话而不是堵住线条。

<a id="receive-notifications"></a>

## 接收通知

使用 [grain 观察员](../grains/observers.md) 来对客户对象进行最佳努力、单向回调。 在交付重要时添加应用级别确认或回收。 当流提供者的订阅和交付模式更适合工作流程时,使用 [串流](../streaming/index.md) 。

<a id="dependency-injection"></a>

## 依赖注射

让 General Host 自己的客户端启动和关闭。 不要按请求创建客户端, 在静态字段中隐藏第二个客户端, 或者处理依赖性输入的单通。 当主机收到终止信号时, 它会关闭客户端作为正常关闭的一部分 。

<a id="observer-invocations-during-shutdown"></a>

## 关闭期间的观察员的援引

优雅客户关闭关闭关闭了新观察者职业的许可, 等待被允许的工作完成后再关闭运输。 排水管包括排队的行业、 运行职业、 间断职业和响应处理。 排队工作保持其每个观察者的秩序。 被接纳的申请工作排水管时取消控制电话仍然可用, 然后 Orleans 关闭和控制路径的排水管。 新请求- 回复电话接收一个 <xref:Orleans.Runtime.SiloUnavailableException>; 被拒绝的单向电话被登录 。

外部客户在等待观察员的祈祷前完成未结外出电话,但有停业例外,允许观察员等待这些电话。 共同接待的客户首先排干他们即将到来的调度队列,然后排干观察员执行。 接来的答复和回召超时继续完成排出电话。 他们自己的自有注射范围在实际完成后解除。

关闭主机的标志将关闭时间绑在等待。 当取消该符号时, 东道方提前关闭程序并拥有根和单子服务的处置权。 Orleans 继续核算被接纳的观察员的工作, 并将共同托管客户的自有范围推迟到工作退出之前处理。 大小 [关闭预算](configuration-guide/shutting-down-orleans.md#configure-a-shutdown-budget) 以完成观察者的任务, 并保持观察员操作和取消回扣的连接 。
