---
title: "配置 .NET 垃圾收集"
description: "配置 .NET 垃圾收集 Orleans silos。"
ms.date: 08/17/2026
ms.topic: how-to
---

<a id="configure-net-garbage-collection"></a>

# 配置 .NET 垃圾收集

[.NET 垃圾收集器(GC)](https://learn.microsoft.com/dotnet/standard/garbage-collection/fundamentals) 分配和收回 Orleans silo管理内存。工作量特性以及可用的CPU和存储决定了最有效的GC配置。

<a id="choose-workstation-or-server-gc"></a>

## 选择工作站或服务器GC

[GC和服务器](https://learn.microsoft.com/dotnet/standard/garbage-collection/workstation-server-gc) 提供不同的资源和输送特点:

- GC工作站以较少的专用GC资源进行收集,适合许多小 silo 流程共用东道主的发展和高密度部署。
- 服务器GC 执行多个堆积层的专用线条和比例收集工作。 它适合优先处理输送量和可缩放性的生产 silos 。

[CoreCLR 默认选择工作站GC](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#workstation-vs-server). 项目SDK设置和部署环境变量确定该程序是使用默认的还是要求服务器GC:

- [`Microsoft.NET.Sdk.Web` 请求服务器GC默认](https://github.com/dotnet/sdk/blob/main/src/WebSdk/ProjectSystem/Targets/Microsoft.NET.Sdk.Web.ProjectSystem.props#L16-L22).
- `Microsoft.NET.Sdk` 和 [`Microsoft.NET.Sdk.Worker`](https://learn.microsoft.com/dotnet/core/extensions/workers#template-defaults) 使用 CoreCLR 默认值。
- [官方 .NET 容器图像](https://github.com/dotnet/dotnet-docker/blob/main/eng/dockerfile-templates/Dockerfile.common-dotnet-envs) 保存可执行项目生成的运行时间配置,并应用部署环境覆盖。

因此,与Web SDK一起建造的集装箱化 silo 要求服务器GC。与基站或工人SDK一起建造的A silo 默认使用工作站GC。在可执行文件 silo 项目中明确设置使预期模式在项目SDKs和容器图像之间保持一致。

当服务器GC 激活时, [适应应用大小的动态适应(DATAS)](https://learn.microsoft.com/dotnet/standard/garbage-collection/datas) 开始一个堆,并随着工作量的变化调整数字,最多不超过该流程可用的处理器数量。 DATAS 默认为活跃状态。固定 [`GCHeapCount`](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-count) 选择固定数量的服务器GC 堆。在 .NET 10 上,设置该选项可以使 `GCDynamicAdaptationMode` 报告 `0`。使用 <xref:System.Runtime.GCSettings.IsServerGC> 来识别GC的口味,并使用具有代表性的负载测试来比较存储器的使用、吞吐和暂停时间。DATAS 的取舍和调试指导见 [准备 .NET 10GC](https://devblogs.microsoft.com/dotnet/preparing-for-dotnet-10-gc/) 。

<a id="configure-the-silo-project"></a>

## 配置 silo 项目

要请求服务器GC, 请将下列属性置于主机 silo的可执行工程中。 文件片断使用类库进行编译:

:::code language="xml" source="../snippets/hosting/Hosting.csproj" id="server_gc":::

[`ServerGarbageCollection` MSBUUD财产](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#workstation-vs-server) 将 `System.GC.Server` 写入主机生成的 `.runtimeconfig.json` 文件。运行时间在主机进程启动时读取此文件 。

[背景GC](https://learn.microsoft.com/dotnet/standard/garbage-collection/background-gc) 生成 2 组在专用线条上进行收藏, 而应用程序线条则继续运行。 它默认地为工作站和服务器GC 启用 。 [`ConcurrentGarbageCollection`](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#background-gc) 设置至 `false` 当代表基准有利于非经常GC时,选择该模式。

[GC理事会组合](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector) 是在运行时间初始化时读取的。在修改项目设置、运行时间配置或环境变量后重新启动进程 。

<a id="container-resources"></a>

## 集装箱资源

.NET 计算运行启动期间集装箱可用的处理器和内存。 可用一个逻辑处理器选择工作站GC。 两个或两个以上可用处理器允许服务器GC请求生效。 由于服务器GC活动, DATAS将参与的堆积计数从一个到现有处理器计数随着工作量需求的变化而变化。

Kubernetes CPU通常要求控制调度和CPU加权。 [Kubernetes 静态 CPU 管理器政策](https://kubernetes.io/docs/tasks/administer-cluster/cpu-management-policies/#static-policy-configuration) 从整数CPU请求中为符合资格的舱点指定了独家CPU贴切性。 .NET 从机器处理器计数、处理近距离和CPU利用率上限计算 <xref:System.Environment.ProcessorCount> ,将一个分数限制舍入下一个整个处理器。 `DOTNET_PROCESSOR_COUNT` 提供明确的值。 [服务器GC 资源设置](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#manage-resource-usage-for-server-gc) 配置使用这些处理器的堆积和线条。见 [.NET 如何确定可用的处理器计数](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/6.0/environment-processorcount-on-windows)。

容器内存限制为默认 [GC当量硬限](https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)提供物理和模拟基础。堆积硬限包括GC型堆积和GC制簿记。 控制式模拟预算的集装箱限量加上本地记忆、套接缓冲、序列缓冲、供应商SDKs、遥测、临时分配暴发和不连续增长。 该主机使GC有能力在平台终止程序之前对喷射作出反应。

以同样的CPU和内存限制进行装载试验。 见 [能力规划和规模](../../deployment/capacity-planning.md) 和 [Kubernetes 资源请求和限制](../../deployment/kubernetes.md#resource-requests-and-limits)。

<a id="verify-the-effective-mode"></a>

## 验证有效模式

使用这些信号验证已部署的运行时间配置 :

- `System.GC.Server: true` 所公布申请的 `.runtimeconfig.json` 记录服务器GC 请求。 没有密钥应用 CoreCLR GC 默认工作站。
- 在启动时, Orleans 日志 `Silo starting with GC settings: ServerGC={ServerGC} GCLatencyMode={GCLatencyMode}` 并发出工作站GC的咨询警告。
- 在运行时, <xref:System.Runtime.GCSettings.IsServerGC> 报告活性GC口味, <xref:System.Environment.ProcessorCount> 报告运行启动期间使用的处理器计数。
- 随着服务器GC的激活, <xref:System.GC.GetConfigurationVariables*> 报告 `GCDynamicAdaptationMode=1` ,而 DATAS 则调整堆肥计数。在 .NET 10上,有固定的堆积计数报告 `0`。

<a id="coordinate-with-orleans-activation-management"></a>

## 与 Orleans 启动管理协调

.NET GC回收无法获取的管理对象。 Orleans [激活收藏](activation-collection.md) 决定闲置的 grain 激活何时不可获取。

- 从工作量和部署量测中选择GC模式。
- Orleans 使得启动活动在默认情况下15分钟不活动后有资格收集;工作量计量指导具体类型年龄调整。
- 启用内存压力激活程序, 以便当进程内存超过配置的限度时优先使用旧的启动功能, 并将使用量移到配置的目标 。

监测分配率、 堆积大小、 暂停时间、 在GC中的时间、 工作设置和激活计数 。 与激活寿命、 应用对象保留和记忆压力缩放同时标出GC 设置 。 见 [.NET GC绩效指导](https://learn.microsoft.com/dotnet/standard/garbage-collection/performance) 和 [Orleans 运行时信号](../monitoring/signals.md)。
