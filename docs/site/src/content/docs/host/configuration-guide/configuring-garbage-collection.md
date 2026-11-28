---
title: 配置 .NET 垃圾回收
description: 为 Orleans Silo 配置 .NET 垃圾回收。
ms.date: 08/17/2026
ms.topic: how-to
---

<a id="configure-net-garbage-collection"></a>

# 配置 .NET 垃圾回收

[.NET 垃圾回收器（GC）](https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals) 为 Orleans Silo 分配和回收托管内存。工作负载特征及可用的 CPU 和内存决定最有效的 GC 配置。

<a id="choose-workstation-or-server-gc"></a>

## 选择工作站 GC 或服务器 GC

[工作站 GC 和服务器 GC](https://learn.microsoft.com/dotnet/standard/garbage-collection/workstation-server-gc)具有不同的资源占用和吞吐量特征：

- 工作站 GC 使用较少的专用 GC 资源执行回收。它适合开发环境，以及多个小型 Silo 进程共享同一宿主的高密度部署。
- 服务器 GC 在专用线程上执行回收，并通过多个堆扩展回收工作。它适合以吞吐量和可伸缩性为优先目标的生产 Silo。

[CoreCLR 默认选择工作站 GC](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#workstation-vs-server)。项目 SDK 设置和部署环境变量决定进程采用该默认值还是请求服务器 GC：

- [`Microsoft.NET.Sdk.Web` 默认请求服务器 GC](https://github.com/dotnet/sdk/blob/main/src/WebSdk/ProjectSystem/Targets/Microsoft.NET.Sdk.Web.ProjectSystem.props#L16-L22)。
- `Microsoft.NET.Sdk` 和 [`Microsoft.NET.Sdk.Worker`](https://learn.microsoft.com/dotnet/core/extensions/workers#template-defaults) 使用 CoreCLR 默认值。
- [官方 .NET 容器镜像](https://github.com/dotnet/dotnet-docker/blob/main/eng/dockerfile-templates/Dockerfile.common-dotnet-envs)保留可执行项目生成的运行时配置，并应用部署环境中的覆盖设置。

因此，使用 Web SDK 构建的容器化 Silo 请求服务器 GC。使用基础 SDK 或 Worker SDK 构建的 Silo 默认采用工作站 GC。在可执行 Silo 项目中显式设置，可在不同项目 SDK 和容器镜像之间保持预期模式一致。

当服务器 GC 生效时，[根据应用程序规模动态调整（DATAS）](https://learn.microsoft.com/dotnet/standard/garbage-collection/datas)从一个堆开始，随工作负载变化调整堆数量，上限为进程可用的处理器数。DATAS 默认启用。固定的 [`GCHeapCount`](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-count) 设置会选择固定数量的服务器 GC 堆。在 .NET 10 上，该设置使 `GCDynamicAdaptationMode` 报告 `0`。使用 <xref:System.Runtime.GCSettings.IsServerGC> 识别 GC 模式，并通过具有代表性的负载测试比较内存使用量、吞吐量及暂停时间。有关 DATAS 的取舍与调优建议，请参阅[为 .NET 10 GC 做准备](https://devblogs.microsoft.com/dotnet/preparing-for-dotnet-10-gc/)。

<a id="configure-the-silo-project"></a>

## 配置 Silo 项目

若要请求服务器 GC，请在承载 Silo 的可执行项目中设置以下属性。文档代码片段使用类库进行编译：

:::code language="xml" source="../snippets/hosting/Hosting.csproj" id="server_gc":::

[`ServerGarbageCollection` MSBuild 属性](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#workstation-vs-server)将 `System.GC.Server` 写入宿主生成的 `.runtimeconfig.json` 文件。运行时在宿主进程启动时读取此文件。

[后台 GC](https://learn.microsoft.com/dotnet/standard/garbage-collection/background-gc)在专用线程上执行第 2 代回收，同时应用程序线程继续运行。工作站 GC 和服务器 GC 默认均启用后台 GC。当具有代表性的基准测试表明非并发 GC 更合适时，将 [`ConcurrentGarbageCollection`](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#background-gc) 设为 `false` 即可选择非并发 GC。

[GC 配置](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector)在运行时初始化时读取。更改项目设置、运行时配置或环境变量后，应重新启动进程。

<a id="container-resources"></a>

## 容器资源

.NET 在运行时启动期间计算容器可用的处理器及内存。只有一个可用逻辑处理器时，选择工作站 GC。两个或更多可用处理器时，服务器 GC 请求才能生效。服务器 GC 生效时，DATAS 随工作负载需求变化，在 1 到可用处理器数量之间调整参与回收的堆数。

Kubernetes CPU 请求通常控制调度及 CPU 权重。[Kubernetes 静态 CPU Manager 策略](https://kubernetes.io/docs/tasks/administer-cluster/cpu-management-policies/#static-policy-configuration)根据整数 CPU 请求为符合条件的 Pod 分配独占 CPU 亲和性。.NET 根据计算机处理器数量、进程亲和性及 CPU 利用率限制计算 <xref:System.Environment.ProcessorCount>，并将小数限制向上舍入到下一个整数处理器数。`DOTNET_PROCESSOR_COUNT` 提供显式数值。[服务器 GC 资源设置](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#manage-resource-usage-for-server-gc)配置使用这些处理器的堆及线程。请参阅[.NET 如何确定可用处理器数量](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/environment-processorcount-on-windows)。

容器内存限制为默认的 [GC 堆硬限制](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)提供物理内存计算依据。堆硬限制涵盖 GC 堆及 GC 内部记账开销。设定容器内存限制时，除了该托管内存预算，还需考虑本机内存、套接字缓冲区、序列化缓冲区、提供程序 SDK、遥测、临时分配峰值及故障转移期间的增长。预留的空间使 GC 能在平台终止进程前有余量应对突发分配。

使用与生产环境相同的 CPU 和内存约束进行负载测试。请参阅[容量规划和伸缩](../../deployment/capacity-planning.md)以及 [Kubernetes 资源请求和限制](../../deployment/kubernetes.md#resource-requests-and-limits)。

<a id="verify-the-effective-mode"></a>

## 验证实际生效的模式

使用以下信号验证部署后的运行时配置：

- 已发布应用程序中，`System.GC.Server: true` 位于 `.runtimeconfig.json` 中，用于记录服务器 GC 请求。如果该键不存在，则采用 CoreCLR 默认的工作站 GC。
- 启动时，Orleans 记录 `Silo starting with GC settings: ServerGC={ServerGC} GCLatencyMode={GCLatencyMode}`，并针对工作站 GC 发出建议性警告。
- 运行时，<xref:System.Runtime.GCSettings.IsServerGC> 报告当前 GC 模式，<xref:System.Environment.ProcessorCount> 报告运行时初始化时使用的处理器数量。
- 服务器 GC 生效时，如果 DATAS 动态调整堆数量，则 <xref:System.GC.GetConfigurationVariables*> 报告 `GCDynamicAdaptationMode=1`。在 .NET 10 上，固定堆数量时报告 `0`。

<a id="coordinate-with-orleans-activation-management"></a>

## 与 Orleans 激活实例管理协同配置

.NET GC 回收不可达的托管对象。Orleans 的[激活实例回收](activation-collection.md)决定空闲 Grain 激活实例何时变为不可达。应配置这两个层次：

- 根据工作负载和部署环境的测量结果选择进程 GC 模式。
- 默认情况下，Orleans 使空闲 15 分钟后的激活实例符合回收条件；根据工作负载测量结果调整特定类型的回收时限。
- 启用内存压力下的激活实例削减，使进程内存超过配置限制时优先停用较旧的激活实例，并使内存用量向配置目标靠拢。

同时监控分配速率、堆大小、暂停时长、GC 耗时、进程工作集以及激活实例数量。结合激活实例生命周期、应用程序对象保留和内存压力削减调整 GC 设置。请参阅 [.NET GC 性能指南](https://learn.microsoft.com/dotnet/standard/garbage-collection/performance)及 [Orleans 运行时信号](../monitoring/signals.md)。
