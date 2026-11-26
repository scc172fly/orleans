---
title: "关闭 Orleans silos"
description: "优雅地停止 Orleans silos 和 .NET 通用主机 。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="shut-down-orleans-silos"></a>

# 关闭 Orleans silos

Orleans 是 <xref:Microsoft.Extensions.Hosting.IHostedService> 内 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host)。当东道方停止时,它会召唤 Orleans' <xref:Microsoft.Extensions.Hosting.IHostedService.StopAsync*> 方法。 Orleans 然后离开集群,关闭网关和网络,停止使用 grains,并阻止供应商以逆生命周期顺序运行。

<a id="graceful-silo-shutdown"></a>

## 优雅 silo 关闭

构建主机 <xref:Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder*> 运行它 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.RunAsync*> 或 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.Run*>都 `Host.CreateApplicationBuilder` 和老年人的(晚年) `Host.CreateDefaultBuilder` 登记簿 <xref:Microsoft.Extensions.Hosting.IHostLifetime> 默认控制台存在使用寿命,所以不需要额外的电话来观察终止请求:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="run_silo":::

配置的 .NET 主机寿命处理相关的终止事件,并启动通用主机关闭程序,因此应用程序不应添加单独的进程退出处理器。详情见 [.NET 通用主机关闭](https://learn.microsoft.com/dotnet/core/extensions/generic-host#host-shutdown)。

在测试或嵌入主机中, 调用 <xref:Microsoft.Extensions.Hosting.HostingAbstractionsHostExtensions.StopAsync*> 并处置主机:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="stop_host":::

不要拨打 <xref:System.Environment.Exit*>,从应用程序代码中切断该程序,或者将 Orleans 服务从主机独立处理。

<a id="configure-a-shutdown-budget"></a>

## 配置关闭预算

主机在关闭期间向包括 Orleans在内的所有主机服务发送取消的标记。 <xref:Microsoft.Extensions.Hosting.HostOptions.ShutdownTimeout> 在配置好的预算之后, 取消该标记。 配置它以适应预期工作量 :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="shutdown_timeout":::

设定管弦乐队终止宽限期比主机关闭超时更长。 包括时间 :

- 装载平衡器和准备状态探测器 停止发送新的交通
- 网关和成员变动以传播。
- Grain 解除回调并撰写状态文章。
- 接受客户-观察员职业及其答复处理。
- 流、提醒、存储和遥测提供者冲洗和停止。

如果外部宽限期先到期, 整个过程就失效了, 优雅的关闭无法完成 。

<a id="make-application-code-shutdown-safe"></a>

## 使应用程序代码安全关闭

- 托管服务、开办任务、生命周期参与者和供应商电话中荣誉取消代号。
- 保持 <xref:Orleans.Grain.OnDeactivateAsync*> 约束;在正常运行期间,持续的重要状态,而不仅仅是关闭。
- 在终止期限之前停止接受新的申请工作。
- 突然终止后,可以安全恢复,因为碰撞和节点损失仍然可能。
- 避免同步屏蔽和无约束回击关闭回击。

Grains 可以在 silo 离开后迁移或重新恢复到其他地方。 不要将优雅的关闭用作整个应用程序的排水屏障, 除非该应用单独协调该行为。

<a id="queued-requests-during-shutdown"></a>

### 关闭期间的有问题请求

当 silo 优雅地关闭了, 激活程序在运行请求完成后仍然有排队的请求, 程序已成功关闭, 并会迁移到另一个 silo将这些请求包含在移民的一揽子申请中。目的地通过启动程序的一般 FFFFO 队列和超负荷检查来承认它们。 grain 取代旧目录; 已经在目标区域排队的请求保留其位置。 调用时 silos 然后记录请求书的新位置, 以便他们的电话在原地址之后等待回复。 silo 组合的左括号。调用保持其原始身份和响应超时。记录新位置是最佳努力:如果最初的通话失败,其拨打者不会记录 silo 以同样的方式,包括按以下方式提出的移民申请: <xref:Orleans.Grain.MigrateOnIdle>。如果信息到启用时间已过期,目的地会拒绝发送请求。过期跟踪是跨越运输和回溯的最好办法,因此延迟的请求可以在拨打者超时后执行。

当移民失败或结果不明时,最初的 silo 拒绝这些要求,因此他们的呼唤者以他们可以重新尝试的拒绝而失败。 如果目的地接收了被拒绝的请求,但答复丢失,则被拒的申请仍可能继续在目的地 silo 上运行。

反之,将转交在启动后无法移徙的申请,其中包括对无国籍工人和无国籍工人的申请。 <xref:Orleans.Placement.ImmovableAttribute> grains,其规模与启动的移民州相绑在一起的请求超过 <xref:Orleans.Configuration.MessagingOptions.MaxMessageBodySize>启动程序启动后抵达的请求,在关闭开始时由于另一个原因已经停止的启动请求,以及迁移到 silo 运行较早版本的早期版本。任意请求- 文本值使用 <xref:Orleans.Configuration.MessagingOptions.MaxMessageHeaderSize> 转交请求的调用人未 <xref:Orleans.Runtime.SiloUnavailableException> 如果原置 silo 响应到达前离开一组,如在回应到达时 silo 坠机

移民是向前推算的,已经转交的请求 <xref:Orleans.Configuration.SiloMessagingOptions.MaxForwardCount> 时间与初始激活保持同步, 当它停止启动时被拒绝, 因此他们的呼叫者在没有执行请求的情况下拒绝失败 。

移民在目的地 silo 的启动时间上增加了两个阶段,即关闭。首先,移民启动时间要等到 <xref:Orleans.Configuration.SiloMessagingOptions.SystemResponseTimeout> 才被接受,包括无同步的外出电话过滤器和目的地的反应。如果该请求时间过后,源拒绝其请求。第二,在目的地接收了请求之后,源头将重新锁定其呼叫者未接听的通话目标。当 <xref:Orleans.Configuration.GrainCollectionOptions.DeactivationTimeout> 过期时,排队的迁移被取消,请求被转交,且认可的迁移完成而没有等待目标的重组。允许主机关闭预算为这两个阶段留出足够的时间。

<a id="containers-and-orchestrators"></a>

## 集装箱和管弦

请求主机关闭前从应用程序流量中删除实例。 然后发送平台正常终止请求,允许主机关闭预算下降,并保留挂断程序强制终止。 强制终止、断电或处理崩溃可以完全绕过主机,因此正确性不能取决于优雅的关闭。

<a id="see-also"></a>

## 另请参阅

订购的 Orleans 回调,见 [Orleans silo 生命周期](../silo-lifecycle.md)。
