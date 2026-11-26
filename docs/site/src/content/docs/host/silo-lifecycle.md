---
title: "Orleans silo 生命周期"
description: "参与 Orleans silo 启动和关闭。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-silo-lifecycle"></a>

# Orleans silo 生命周期

Orleans 启动和停止运行时组件,通过一个有顺序的可见生命周期。启动前先从最低阶段推进到最高阶段。关闭后将同时进行相同的阶段。

应用代码大多应该使用 <xref:Microsoft.Extensions.Hosting.IHostedService> 或 <xref:Microsoft.Extensions.Hosting.BackgroundService>执行 <xref:Orleans.ILifecycleParticipant`1> 与 <xref:Orleans.Runtime.ISiloLifecycle> 只有在部件必须在 Orleans 运行时生命周期。

<a id="stages"></a>

## 各阶段

<xref:Orleans.ServiceLifecycleStage> defines these stages:

| 阶段 | 价值值值 | 目的目的宗旨 |
|---|---:|---|
| <xref:Orleans.ServiceLifecycleStage.First> | `int.MinValue` | 最悬浮生命周期阶段 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeInitialize> | `2000` | 初始化运行时间 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeServices> | `4000` | 启动核心网络和运行时服务 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeStorageServices> | `6000` | 启动运行时存储服务 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeGrainServices> | `8000` | 开始 grain- 附加运行时服务 |
| <xref:Orleans.ServiceLifecycleStage.AfterRuntimeGrainServices> | `8100` | grain 运行时服务后运行 |
| <xref:Orleans.ServiceLifecycleStage.ApplicationServices> | `10000` | 启动应用程序服务 |
| <xref:Orleans.ServiceLifecycleStage.ValidateInitialConnectivity> | `19900` | 在启动前验证连接 |
| <xref:Orleans.ServiceLifecycleStage.GrainDirectoryShutdown> | `19997` | 坐标 grain- 指令关闭 |
| <xref:Orleans.ServiceLifecycleStage.GrainDeactivation> | `19998` | 关闭时停止活动 grains |
| <xref:Orleans.ServiceLifecycleStage.BecomeActive> | `19999` | 启动前立即进行内部过渡 |
| <xref:Orleans.ServiceLifecycleStage.Active> | `20000` | Silo 是活跃的,接受工作量 |
| <xref:Orleans.ServiceLifecycleStage.Last> | `int.MaxValue` | 最终生命周期阶段 |

有些常数有意共享或括号很紧的值, 因为它们的启动和关闭语义不同。 将命名的常数作为订购合同; 不要将其数字值复制到应用代码中 。

> [!IMPORTANT]
> <xref:Orleans.ServiceLifecycleStage.BecomeActive> 保留给集群成员关系和网关的状态转换。应用组件通常使用 <xref:Orleans.ServiceLifecycleStage.ApplicationServices> 或 <xref:Orleans.ServiceLifecycleStage.Active>。

<a id="silo-lifecycle-participation"></a>

## Silo 生命周期参与

登记一个实施 <xref:Orleans.ILifecycleParticipant`1> 用于 <xref:Orleans.Runtime.ISiloLifecycle>:

<a id="example"></a>

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="lifecycle_participant":::

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="register_lifecycle_participant":::

启动回调必须在 Orleans 预付款到下一阶段之前完成。关闭时,停止回调会收到主机关闭取消标记 。

<xref:Orleans.Runtime.ISiloLifecycle.HighestCompletedStage> and <xref:Orleans.Runtime.ISiloLifecycle.LowestStoppedStage> expose lifecycle progress for diagnostics.

<a id="choose-a-stage"></a>

## 选择一个阶段

- 使用 <xref:Orleans.ServiceLifecycleStage.ApplicationServices> 依赖性,必须从核心开始 Orleans 在那日,你们必在他们的前面逗留一会儿。
- 使用 <xref:Orleans.ServiceLifecycleStage.Active> 当工作需要活跃 silo 和 grain 电话
- 避免运行阶段,除非实施依赖于特定 Orleans 子系统的基础设施。
- 不要在 <xref:Orleans.ServiceLifecycleStage.BecomeActive>、 <xref:Orleans.ServiceLifecycleStage.GrainDeactivation>或 <xref:Orleans.ServiceLifecycleStage.GrainDirectoryShutdown>订阅应用程序代码。

当需要的受抚养人无法初始化时启动失败。 对于可选或持续重试的工作, 必须在 Orleans 之后启动托管的背景服务, 而不是无限期地阻碍生命周期阶段 。

<a id="logging"></a>

## 伐木

`Orleans.Runtime.SiloLifecycleSubject` 登录器类别报告参与者、时间和错误,并按阶段报告。设置 <xref:Microsoft.Extensions.Logging.LogLevel> 到 `Information` ,同时诊断启动订单或慢停机。生命周期回调应记录他们等待的外部操作并取消荣誉。

实施详情见 [Orleans 生命周期](../implementation/orleans-lifecycle.md)。简单的一次性回调见 [背景服务和启动任务](configuration-guide/startup-tasks.md)。主机终止行为见 [关闭 Orleans silos](configuration-guide/shutting-down-orleans.md)。
