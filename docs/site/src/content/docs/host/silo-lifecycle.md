---
title: Orleans Silo 生命周期
description: 参与 Orleans Silo 的启动和关闭过程。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-silo-lifecycle"></a>

# Orleans Silo 生命周期

Orleans 通过具有明确顺序且可观察的生命周期来启动和停止运行时组件。启动从最低阶段推进到最高阶段，关闭则按相反顺序执行相同阶段。

大多数应用程序代码应使用 <xref:Microsoft.Extensions.Hosting.IHostedService> 或 <xref:Microsoft.Extensions.Hosting.BackgroundService>。只有组件必须在 Orleans 运行时生命周期中的精确位置运行时，才应实现 <xref:Orleans.ILifecycleParticipant`1>，并使用 <xref:Orleans.Runtime.ISiloLifecycle> 作为其生命周期类型。

<a id="stages"></a>

## 阶段

<xref:Orleans.ServiceLifecycleStage> 定义了以下阶段：

| 阶段 | 值 | 用途 |
|---|---:|---|
| <xref:Orleans.ServiceLifecycleStage.First> | `int.MinValue` | 最早的生命周期阶段 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeInitialize> | `2000` | 初始化运行时 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeServices> | `4000` | 启动核心网络通信及运行时服务 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeStorageServices> | `6000` | 初始化运行时存储服务 |
| <xref:Orleans.ServiceLifecycleStage.RuntimeGrainServices> | `8000` | 启动面向 Grain 的运行时服务 |
| <xref:Orleans.ServiceLifecycleStage.AfterRuntimeGrainServices> | `8100` | 在 Grain 运行时服务之后运行 |
| <xref:Orleans.ServiceLifecycleStage.ApplicationServices> | `10000` | 启动应用层服务 |
| <xref:Orleans.ServiceLifecycleStage.ValidateInitialConnectivity> | `19900` | 在进入活动状态前验证连接 |
| <xref:Orleans.ServiceLifecycleStage.GrainDirectoryShutdown> | `19997` | 协调 Grain 目录的关闭 |
| <xref:Orleans.ServiceLifecycleStage.GrainDeactivation> | `19998` | 在关闭期间停用 Grain |
| <xref:Orleans.ServiceLifecycleStage.BecomeActive> | `19999` | 紧接进入活动状态之前的内部转换 |
| <xref:Orleans.ServiceLifecycleStage.Active> | `20000` | Silo 已处于活动状态并接受工作负载 |
| <xref:Orleans.ServiceLifecycleStage.Last> | `int.MaxValue` | 最后的生命周期阶段 |

部分常量有意共享相同的值或使用非常接近的值，因为其启动和关闭语义有所不同。将命名常量视为排序契约；不要将其数值直接复制到应用程序代码中。

> [!IMPORTANT]
> <xref:Orleans.ServiceLifecycleStage.BecomeActive> 保留用于成员资格和网关的状态转换。应用程序组件通常使用 <xref:Orleans.ServiceLifecycleStage.ApplicationServices> 或 <xref:Orleans.ServiceLifecycleStage.Active>。

<a id="silo-lifecycle-participation"></a>

## 参与 Silo 生命周期

注册一个实现 <xref:Orleans.ILifecycleParticipant`1> 的单例，其生命周期类型为 <xref:Orleans.Runtime.ISiloLifecycle>：

<a id="example"></a>

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="lifecycle_participant":::

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="register_lifecycle_participant":::

启动回调必须完成，Orleans 才会推进到下一阶段。在关闭期间，停止回调接收宿主关闭取消令牌。

<xref:Orleans.Runtime.ISiloLifecycle.HighestCompletedStage> 和 <xref:Orleans.Runtime.ISiloLifecycle.LowestStoppedStage> 公开生命周期进度，供诊断使用。

<a id="choose-a-stage"></a>

## 选择阶段

- 对于必须在 Orleans 核心服务之后启动、在它们之前停止的依赖项，使用 <xref:Orleans.ServiceLifecycleStage.ApplicationServices>。
- 当工作需要活动 Silo 和 Grain 调用时，使用 <xref:Orleans.ServiceLifecycleStage.Active>。
- 除非实现依赖特定 Orleans 子系统的基础设施，否则避免使用运行时阶段。
- 不要让应用程序代码订阅 <xref:Orleans.ServiceLifecycleStage.BecomeActive>、<xref:Orleans.ServiceLifecycleStage.GrainDeactivation> 或 <xref:Orleans.ServiceLifecycleStage.GrainDirectoryShutdown> 阶段。

如果必需的依赖项无法初始化，则应使启动失败。对于可选工作或需要持续重试的工作，应在 Orleans 之后启动宿主后台服务，而不是无限期阻塞生命周期阶段。

<a id="logging"></a>

## 日志记录

`Orleans.Runtime.SiloLifecycleSubject` 日志类别按阶段报告参与者、耗时及错误。诊断启动顺序或关闭缓慢的问题时，将 <xref:Microsoft.Extensions.Logging.LogLevel> 设置为 `Information`。生命周期回调应记录正在等待的外部操作，并遵循取消请求。

有关实现细节，请参阅[Orleans 生命周期](../implementation/orleans-lifecycle.md)。如需更简单的一次性回调，请参阅[后台服务与启动任务](configuration-guide/startup-tasks.md)。有关宿主终止行为，请参阅[关闭 Orleans Silo](configuration-guide/shutting-down-orleans.md)。
