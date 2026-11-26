---
title: "Grain 目录迁移"
description: "更新 Orleans grain 目录配置,同时保存激活和分区特性。"
ms.date: 10/21/2026
ms.topic: how-to
---

<a id="grain-directory-migration"></a>

# Grain 目录迁移

<a id="local-to-distributed-directory-upgrade"></a>
<a id="legacy-to-distributed-directory-upgrade"></a>

Orleans 11.0.0-预览.1 使用分布式 grain 默认情况下目录。对于 Orleans 10组使用 `LegacyGrainDirectory`,将运行时间升级与目录迁移分开:第一次更新每次运行时间,同时保留遗留目录,然后在第二次推出时移动目录。

对于已经使用分布式目录的组群,请遵循 [更新现有的分配式指令类集群](#upgrade-existing-distributed-directory-clusters) 来保留其分区配置。

在选择滚动运行时间升级之前,使用 [部署兼容性指导](deployment-and-rollback.md#compatibility-boundary) 来限定准确运行时间和应用版本。

<a id="prepare-and-defer-migration"></a>

## 准备和推迟移徙

1. 清查每个 grain 类型所选的默认和命名目录。 在此过程中保持指定提供者的选择和群集身份不变 。
1. 符合以下条件:有代表性的通信中具有序列合同、持续供应商和部署配置;在启动期间核实成功通话,优雅地 silo 离开,完成后重复分辨率和目录完整性。
1. 恢复和启动复苏的预算能力需要的持久性状态。 在混合指令阶段,临时重复激活可以同时运行。 使用持久性同值货币控制和无能外部操作来容忍同时启动和回溯。
1. 推出新的运行时间 <xref:Orleans.Hosting.CoreHostingExtensions.UseLegacyGrainDirectory*> 配置于 **每替换 silo**。此选项保留了运行时版本同时存在的遗留目录。也保持该配置用于缩放实例。
1. 等到每个 silo 运行新的运行时间,会籍稳定, grain-呼叫和激活行为与基线匹配。应用程序可以保持这种全传配置,直到目录迁移准备就绪为止。

`UseLegacyGrainDirectory` 推迟迁移。 在目录迁移开始之前选择它。 请使用其 API 引用来表示配置表面; 主机片段使用的释放包在 API之前就已经过期了 。

<a id="migrate-the-directory"></a>

## 迁移目录

1. 对于这个要分发的遗留推出,请将 <xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo> 保留在 `1` 并保留默认的 <xref:Orleans.Configuration.GrainDirectoryOptions.GetPartitionBoundaries> 功能。
1. 从部署配置中删除 `UseLegacyGrainDirectory` 。运行时默认选择分布目录;明确呼叫 <xref:Orleans.Hosting.CoreHostingExtensions.AddDistributedGrainDirectory*> 也可以选择它。
1. 一次替换 silos 的捆绑数,保留测试过的备抵silo 和快速能力底层。 严格停止每次离开 silo,允许在下次替换之前完成成员资格和目录所有权转让。
1. 在每一个混合指令阶段,运行现有的 grains 和新激活的 grains 并监测呼叫故障和启动批次。
1. 继续使用分布式目录直到每个 silo 都使用分布式目录, 然后允许会籍、 范围恢复和重复分辨率相交。 核查每一次激活一个已登记的激活 grain , 并匹配活动 grains的登记。 保持分配式选择和分区配置在以后部署和扩展时的配置 。

兼容系统目标在过渡期内连接了两个目录协议。 分布式默认 silo 通过其分布式目录服务于遗留目录 silos 的远程请求。 遗留目录 silos 暴露分配分区请求和激活恢复的终点。 在两个推出阶段中保持命名提供者选择的一致性 。

对于在整个削减过程中需要独家启动所有权的工作量,使用协调的全组重新启动而不是滚动目录迁移:优雅地停止每一个 silo,然后开始分配违约部署。

<a id="watch-the-migration"></a>

## 看着移徙

比较对比 grain- 调用错误率和延时率、激活热量和故障、重复启动报告、成员人数和升级前基线的目录登记错误。 silo 离开。

升级完成后,临时混合阶段重复应解决。最后的健康检查站有一个分布全面的集群、稳定的会员观、完整的恢复、成功呼叫现有和新的 grains,以及一个登记在册的每次单一活动启动点 grain。

超过应用安全或可用性阈值时暂停替换。 保存目录和成员日志并保留足够的分布式指令能力。 调查在全部分配的集群聚合后持续存在的重复激活或登记错配。

<a id="recovery-and-one-way-migration"></a>

## 复苏和单向移徙

集开始使用分布式默认值后, 继续向全分配式组合群前进。 将该组返回到遗留目录是没有支持的。 决定回收程序并在引入第一个分布式默认值 silo之前对部署进行资格认证 。

使用合格回收程序,同时保留分布式目录及其分区配置的回收应用状态。 在该计划中包含兼容的应用图像、存储变化和回收点。

<a id="upgrade-existing-distributed-directory-clusters"></a>

## 更新现有的分配式指令类集群

正在滚动时间更新现有的分布式指令集, 既保存分区数, 也保存从散列边界向分隔号的映射。 目录请求和所有权转移快照针对那些编号的分区, 所以所有 silos 必须就它们的范围达成一致 。

Orleans 10.1 使用每个 silo 30个分区和分配的分区编号,在用未符号值对每个 silo的统一散列进行排序后。 Orleans 10.2 将默认数改为一个,并按散发顺序指定多个分区。

当正在升级分布式指令集集时 Orleans 10.1, 配置新器 silos 与 <xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo> 设置至 `30` 和定义应用程序的 <xref:Orleans.Configuration.GrainDirectoryOptions.GetPartitionBoundaries> 代表返回每个 silo单顺序的制服杂交:

:::code language="csharp" source="../snippets/compiled/Deployment/DirectoryPartitioningSnippet.cs" id="legacy_directory_partitions":::

排序不可变散列时, 保存 silo的缓存散列顺序并生成现有分区身份。 在升级后并在每个兼容的分配目录恢复部署中保留此配置 。

已经使用 Orleans 10.2和10.2-later生成的顺序绘图的集群保留默认边界功能及其现有的分区计数。 符合运行时间升级和在持续交通下兼容的应用恢复条件, 检查目录登记保存和激活独有性。 改变固定的分组分区计数或边界图需要协调全组重新启用或进行带控制 grain- State所有权的蓝绿色切换。

<a id="rebuild-uniform-hash-callers"></a>

## 重建统一打呼机

<a id="uniform-silo-hash-api-compatibility"></a>

<xref:Orleans.Runtime.SiloAddress.GetUniformHashCodes*> 返回一个 <xref:System.Collections.Immutable.ImmutableArray`1> , 与 silo共享缓存散列存储。重建呼叫器时根据 `uint[]` 返回类型编译。当需要可变缓冲时使用 `ToArray()` ,或 `Sort()` 以获得排序的不可更改的结果,同时保存缓存值。

目录目前的配置和结构,见 [Grain 目录](../host/grain-directory.md) 和 [Grain 目录结构](../implementation/grain-directory.md)。
