---
title: Orleans 客户端
description: 选择和托管 Orleans 客户端。
ms.date: 08/18/2026
ms.topic: concept-article
---

<a id="orleans-clients"></a>

# Orleans 客户端

Orleans 客户端让应用程序代码能够调用 Grain、使用流并访问其他集群服务。Orleans 在每个 Silo 内自动提供客户端，也可以在独立进程中托管外部客户端。

<a id="choose-a-hosting-model"></a>

## 选择托管模式

| 模式 | 优先使用的场景 | 权衡 |
|---|---|---|
| 共同托管的客户端 | HTTP 终结点、后台工作器和 Grain 可以共享同一进程。 | 拓扑最简单，调用速度最快，但客户端工作负载与 Silo 共享 CPU 和内存。 |
| 外部客户端 | 前端和 Silo 需要独立扩缩容、部署、安全或资源隔离。 | 增加网关、网络跳转及需要运维的进程。 |

除非存在隔离需求，否则从共同托管的客户端开始。

<a id="co-hosted-clients"></a>

## 共同托管的客户端

<a id="obtain-a-client-from-a-host"></a>

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 在宿主的服务提供程序中注册 <xref:Orleans.IClusterClient> 和 <xref:Orleans.IGrainFactory>：

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="local_silo_and_client":::

来自共同托管客户端的调用使用 Silo 已知的集群信息，无须经过网关。如果目标激活实例位于本地，Orleans 还可以避免一次网络跳转。

<a id="call-grains-from-aspnet-core"></a>

### 从 ASP.NET Core 调用 Grain

`orleans-web` 模板创建一个 ASP.NET Core 应用程序，共同托管一个 Silo，并通过 HTTP 终结点公开一个 Grain：

```dotnetcli
dotnet new install Microsoft.Orleans.Templates
dotnet new orleans-web --name HelloOrleans --output HelloOrleans
dotnet run --project HelloOrleans/HelloOrleans.csproj
```

从另一个终端调用该终结点：

```console
curl http://localhost:5000/hello/Ada
```

终结点返回 `Hello, Ada!`。ASP.NET Core 通过依赖注入解析 <xref:Orleans.IGrainFactory>，终结点获取以 `Ada` 为键的 Grain 引用，Orleans 在请求调用它时激活 Grain。

生成的应用程序使用 localhost 集群配置创建单节点开发集群。对于多 Silo 部署，配置[共享的集群和存储提供程序](configuration-guide/typical-configurations.md)，并遵循[生产就绪检查清单](../deployment/production-readiness.md)。

<a id="external-clients"></a>

## 外部客户端

<a id="initialize-a-grain-client"></a>
<a id="example"></a>

安装 [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client)，然后在 [.NET 通用宿主](https://learn.microsoft.com/dotnet/core/extensions/generic-host)中使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 添加客户端：

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="external_client":::

客户端在宿主启动期间连接，之后可通过 [.NET 依赖注入](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)获取。将使用 Orleans 的托管服务注册在 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 之后，使宿主在客户端启动后再启动这些服务：

:::code language="csharp" source="snippets/ClusterClientHostedService.cs" id="cluster_client_hosted_service":::

有关集群和网关设置，请参阅[客户端配置](configuration-guide/client-configuration.md)。

<a id="client-connectivity"></a>

## 客户端连接

Orleans 包含默认的连接重试筛选器。在初始启动期间，它使用线性退避策略重试符合条件的连接失败，最多重试 15 次。如果耗尽这些重试机会，宿主启动失败，而不是在没有集群连接时仍表现为健康状态。

可以使用 <xref:Orleans.Hosting.ClientBuilderExtensions.UseConnectionRetryFilter*> 或 <xref:Orleans.IClientConnectionRetryFilter> 替换默认策略。自定义策略应当：

- 仅重试暂时性故障。
- 限制尝试次数或总时长。
- 遵循传入的取消令牌。
- 集群持续不可用或配置持续无效时，允许启动失败。

启动取消会中断网关发现、连接尝试及初始集群清单检索。客户端在被取消的启动操作完成之前停止初始化工作并释放部分启动的客户端资源。启动成功后，网关和清单的刷新遵循客户端生命周期。

自定义网关提供程序实现 <xref:Orleans.Messaging.IGatewayListProvider.InitializeGatewayListProviderAsync*> 和 <xref:Orleans.Messaging.IGatewayListProvider.GetGatewaysAsync*>，并将传入的令牌传给支持取消的 I/O 操作。对于实现不带令牌方法的提供程序，默认接口实现会取消调用方的等待，并跟踪提供程序操作的最终完成情况。

<a id="client-lifetime-and-registration"></a>

### 客户端生命周期与注册

对于通用宿主和 ASP.NET Core 应用程序，将 <xref:Orleans.IClusterClient> 作为应用程序进程生命周期内的单例。使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 注册一次，然后通过依赖注入解析，而不是为每个请求或控制器创建另一个客户端，或在静态字段中缓存额外的客户端。

Web 应用程序或工作器可以在所有请求及后台服务之间安全地共享同一客户端，因为 Orleans 旨在支持多线程并发使用。客户端可安全地在多线程间复用；只需保护在 Orleans 之外共享的可变应用程序状态。

让宿主管理客户端启动和关闭。宿主在应用程序启动时启动客户端，并在正常终止时关闭它，因此不应手动释放通过依赖注入获得的单例。如果集群在启动期间不可用，宿主会快速失败，而不是使应用程序停留在部分启动的状态。

启动之后，Orleans 随集群成员资格变化刷新网关并重新连接。如果网关或 Silo 不可用，客户端会尝试自动重连，单次 Grain 调用仍可能暴露暂时性故障。Grain 引用仍然有效，但仅在应用程序可以安全容忍重复执行时才重试操作。

<a id="make-calls-to-grains"></a>

## 调用 Grain

外部客户端代码不受 Grain 基于执行轮次的并发模型约束。多个线程可以并发使用 <xref:Orleans.IClusterClient> 和 Grain 引用。使用普通 .NET 同步机制保护客户端侧的可变状态。

Grain 调用返回 <xref:System.Threading.Tasks.Task>、<xref:System.Threading.Tasks.Task`1>、<xref:System.Threading.Tasks.ValueTask> 或 <xref:System.Threading.Tasks.ValueTask`1>，具体取决于[Grain 接口规则](../grains/index.md)。始终异步等待调用完成，而不是阻塞线程。

<a id="receive-notifications"></a>

## 接收通知

使用[Grain 观察者](../grains/observers.md)向客户端对象发送尽力而为的单向回调。如果投递很重要，添加应用程序级确认或恢复机制。当流提供程序的订阅和投递模型更适合工作流程时，使用[流](../streaming/index.md)。

<a id="dependency-injection"></a>

## 依赖注入

让通用宿主管理客户端启动和关闭。不要为每个请求创建客户端，不要在静态字段中缓存另一个客户端，也不要释放通过依赖注入获得的单例。宿主收到终止信号时，会在正常关闭过程中关闭客户端。

<a id="observer-invocations-during-shutdown"></a>

## 关闭期间的观察者调用

客户端优雅关闭时，不再接纳新的观察者调用，并在关闭传输前等待已接纳的工作完成。排空过程包括已排队、正在执行、交错执行的调用以及响应处理。排队工作保持各观察者内部的顺序。已接纳的应用程序工作排空期间，取消控制调用仍可用；随后 Orleans 关闭并排空该控制路径。新的请求-响应调用会收到 <xref:Orleans.Runtime.SiloUnavailableException>；被拒绝的单向调用会被记录到日志中。

外部客户端先使用关闭异常结束尚未完成的传出调用，再等待观察者调用，使正在等待这些调用的观察者得以完成。共同托管客户端先排空传入分派队列，再排空观察者执行。在此排空期间，传入响应和回调超时仍会使其传出调用完成。它们拥有的依赖注入作用域在执行真正结束后释放。

宿主关闭令牌限制关闭等待的时长。该令牌被取消时，宿主继续其关闭流程，并负责释放根服务和单例服务。Orleans 仍然跟踪已接纳的观察者工作，并将共同托管客户端拥有的作用域的释放推迟到这些工作退出之后。为观察者完成设置足够的[关闭时间预算](configuration-guide/shutting-down-orleans.md#configure-a-shutdown-budget)，并限制观察者操作和取消回调的执行时长。
