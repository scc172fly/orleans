---
title: 关闭 Orleans Silo
description: 使用 .NET 通用宿主优雅地停止 Orleans Silo。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="shut-down-orleans-silos"></a>

# 关闭 Orleans Silo

Orleans 是一个 <xref:Microsoft.Extensions.Hosting.IHostedService>，运行在 [.NET 通用宿主](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 中。当宿主停止时，它会调用 Orleans 的 <xref:Microsoft.Extensions.Hosting.IHostedService.StopAsync*> 方法。随后 Orleans 离开集群，关闭网关和网络连接，停用 Grain，并按生命周期的相反顺序停止各个提供程序。

<a id="graceful-silo-shutdown"></a>

## 优雅地关闭 Silo

使用 <xref:Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder*> 构建宿主，并使用 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync*> 或 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run*> 运行它。`Host.CreateApplicationBuilder` 与较旧的 `Host.CreateDefaultBuilder` 默认都会为控制台生命周期注册 <xref:Microsoft.Extensions.Hosting.IHostLifetime>，因此无需额外调用即可观察终止请求：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="run_silo":::

已配置的 .NET 宿主生命周期处理相关终止事件，并启动通用宿主的关闭流程，因此应用程序不应添加单独的进程退出处理程序。有关详细信息，请参阅[.NET 通用宿主关闭](https://learn.microsoft.com/dotnet/core/extensions/generic-host#host-shutdown)。

在测试或嵌入式宿主中，调用 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.StopAsync*>，然后释放宿主：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="stop_host":::

不要调用 <xref:System.Environment.Exit*>，不要从应用程序代码中强制终止进程，也不要绕过宿主单独释放 Orleans 服务。

<a id="configure-a-shutdown-budget"></a>

## 配置关闭时间预算

在关闭期间，宿主向包括 Orleans 在内的每个托管服务传递取消令牌。<xref:Microsoft.Extensions.Hosting.HostOptions.ShutdownTimeout> 会在配置的时间预算耗尽后取消该令牌。根据预期工作负载配置此值：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="shutdown_timeout":::

将编排器的终止宽限期设置为长于宿主关闭超时时间。预留以下操作所需的时间：

- 负载均衡器和就绪探测停止发送新流量。
- 网关与成员资格变更传播。
- Grain 停用回调和状态写入。
- 已准入的客户端观察者调用及其响应处理。
- 流、提醒器、存储和遥测提供程序刷新缓冲数据并停止。

如果外部宽限期先耗尽，进程将被强制终止，无法完成优雅关闭。

<a id="make-application-code-shutdown-safe"></a>

## 确保应用程序代码能够安全关闭

- 在托管服务、启动任务、生命周期参与者及提供程序调用中遵循取消令牌。
- 限制 <xref:Orleans.Grain.OnDeactivateAsync*> 的执行时间；在正常运行期间持久化重要状态，而不是仅依赖关闭过程。
- 在终止截止时间之前停止接受新的应用程序工作。
- 确保突然终止后的恢复过程安全，因为仍可能发生崩溃和节点丢失。
- 避免在关闭回调中进行同步阻塞和无界重试。

Silo 离开集群后，Grain 可以迁移或在其他位置重新激活。除非应用程序另外协调这一行为，否则不要将优雅关闭视为应用程序范围内排空全部工作的屏障。

<a id="queued-requests-during-shutdown"></a>

### 关闭期间排队的请求

当 Silo 优雅关闭时，如果某个激活实例在正在运行的请求结束后仍有排队的请求，该实例就会迁移到另一个 Silo，并在迁移包中携带这些请求。目标 Silo 通过激活实例的普通 FIFO 队列和过载检查接纳它们。新激活实例在注册到 Grain 目录、替换旧实例之前收到这些请求；目标侧已排队的请求保留原有位置。随后，发起调用的 Silo 记录请求的新位置，使原始 Silo 离开集群后调用仍继续等待响应。调用保留原有标识和响应超时。记录新位置属于尽力而为操作：如果调用方未记录新位置，且原始 Silo 先离开集群，则调用失败。所有迁移都以相同方式携带激活实例中的排队请求，包括通过 <xref:Orleans.Grain.MigrateOnIdle> 请求的迁移。目标 Silo 会丢弃消息生存时间已过期的请求。跨传输和重试的过期跟踪属于尽力而为，因此延迟的请求可能在调用方超时后仍然执行。

当迁移失败或其结果未知时，原始 Silo 会拒绝这些请求，因此调用方收到可重试的拒绝并使调用失败。如果目标 Silo 已接收请求但其响应丢失，被拒绝的请求仍可能在目标 Silo 上运行。

无法随激活实例迁移的请求会改为转发。这包括针对无状态工作者和 <xref:Orleans.Placement.ImmovableAttribute> Grain 的请求、请求大小上界与激活实例迁移状态的总大小超过 <xref:Orleans.Configuration.MessagingOptions.MaxMessageBodySize> 的请求、激活实例开始迁移后到达的请求、关闭开始时已因其他原因停用中的激活实例所收到的请求，以及迁移到运行早期版本 Silo 的请求。任意请求上下文值均使用 <xref:Orleans.Configuration.MessagingOptions.MaxMessageHeaderSize> 作为其保守的消息头大小上界。如果原始 Silo 在响应到达之前离开集群，转发请求的调用方将收到 <xref:Orleans.Runtime.SiloUnavailableException> 而失败，正如 Silo 崩溃时一样。

一次迁移计为一次转发。已转发 <xref:Orleans.Configuration.SiloMessagingOptions.MaxForwardCount> 次的请求留在原始激活实例上，并在该实例停用时被拒绝，因此调用方因请求被拒绝而失败，请求不会执行。

迁移分两个阶段，将目标 Silo 上的激活时间计入关闭耗时。首先，迁移中的激活实例最多等待 <xref:Orleans.Configuration.SiloMessagingOptions.SystemResponseTimeout>，以完成接收调用，包括异步传出调用筛选器及目标 Silo 的响应。如果该调用超时，源 Silo 会拒绝相应请求。其次，目标 Silo 接收请求后，源 Silo 更新调用方待完成调用的目标位置。当 <xref:Orleans.Configuration.GrainCollectionOptions.DeactivationTimeout> 耗尽时，排队中的迁移被取消，其请求被转发；已获接收的迁移则直接完成，无需等待调用目标更新结束。应在宿主关闭时间预算中为这两个阶段留出足够时间。

<a id="containers-and-orchestrators"></a>

## 容器与编排器

在请求关闭宿主之前，先将该实例从应用程序流量中移除。然后发送平台正常的终止请求，允许宿主关闭时间预算耗尽，仅对无响应的进程采取强制终止。强制终止、电源中断或进程崩溃可能完全绕过宿主，因此正确性不得依赖优雅关闭。

<a id="see-also"></a>

## 另请参阅

有关具有明确顺序的 Orleans 回调，请参阅[Orleans Silo 生命周期](../silo-lifecycle.md)。
