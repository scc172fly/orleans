---
title: Grain 目录迁移
description: 升级 Orleans Grain 目录配置，同时保留激活实例和分区标识。
ms.date: 10/21/2026
ms.topic: how-to
---

<a id="grain-directory-migration"></a>

# Grain 目录迁移

<a id="local-to-distributed-directory-upgrade"></a>
<a id="legacy-to-distributed-directory-upgrade"></a>

Orleans 11.0.0-preview.1 默认使用分布式 Grain 目录。对于使用 `LegacyGrainDirectory` 的 Orleans 10 集群，应将运行时升级与目录迁移分开：先升级所有运行时，同时保留旧版目录，再通过第二次发布迁移目录。

对于已经使用分布式目录的集群，请按照[升级现有分布式目录集群](#upgrade-existing-distributed-directory-clusters)中的说明保留其分区配置。

选择滚动运行时升级之前，请使用[部署兼容性指南](deployment-and-rollback.md#compatibility-boundary)验证确切的运行时版本和应用程序版本。

<a id="prepare-and-defer-migration"></a>

## 准备并推迟迁移

1. 盘点每种 Grain 类型所选择的默认目录及命名目录。在此过程中保持命名提供程序的选择和集群标识不变。
1. 在具有代表性的流量下验证序列化契约、持久化提供程序及部署配置。验证发布期间的成功调用、Silo 正常离开，以及发布完成后的重复激活实例消解与目录完整性。
1. 持久化重新激活后所需的状态，并为激活实例恢复预留容量。在混合目录阶段，临时重复激活实例可能并发运行。使用持久化并发控制及幂等外部操作，容忍并发激活和重试。
1. 发布新运行时，并在**每个替换 Silo** 上配置 <xref:Orleans.Hosting.CoreHostingExtensions.UseLegacyGrainDirectory*>。这样可在运行时版本共存期间保留旧版目录。横向扩容的新实例也应保留该配置。
1. 等待所有 Silo 均运行新运行时、成员资格稳定，且 Grain 调用及激活行为与基线一致。应用程序可继续采用这一全旧版目录配置，直至准备好迁移目录。

`UseLegacyGrainDirectory` 用于推迟迁移。应在目录迁移开始前选择它。请通过其 API 参考了解配置接口；托管代码片段使用的已发布软件包早于此 API。

<a id="migrate-the-directory"></a>

## 迁移目录

1. 对于此次从旧版目录向分布式目录的发布，将 <xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo> 保持为 `1`，并保留默认的 <xref:Orleans.Configuration.GrainDirectoryOptions.GetPartitionBoundaries> 函数。
1. 从部署配置中移除 `UseLegacyGrainDirectory`。运行时默认选择分布式目录；显式调用 <xref:Orleans.Hosting.CoreHostingExtensions.AddDistributedGrainDirectory*> 也可选择它。
1. 每次替换有限数量的 Silo，维持经过验证的就绪 Silo 数量和额外容量下限。正常停止每个即将离开的 Silo，等待成员资格更新和目录所有权转移完成后再进行下一次替换。
1. 在每个混合目录阶段，对现有 Grain 和新激活的 Grain 执行调用，并监视调用失败和激活实例变动。此阶段允许出现临时重复激活实例。
1. 持续进行，直至所有 Silo 均使用分布式目录，然后等待成员资格、范围恢复及重复激活实例消解收敛。验证每个单激活实例 Grain 只有一个已注册的激活实例，并且活动 Grain 的注册信息相匹配。在后续部署和横向扩容实例中保留分布式目录选择及分区配置。

兼容性系统目标在这一过渡期间桥接两种目录协议。默认使用分布式目录的 Silo 通过其分布式目录处理来自旧版目录 Silo 的远程请求。旧版目录 Silo 公开用于分布式分区请求和激活实例恢复的终结点。在两次发布期间始终保持命名提供程序的选择一致。

对于要求在整个切换期间拥有排他激活实例所有权的工作负载，应采用协调的全集群重启，而非滚动目录迁移：正常停止全部 Silo，再启动默认使用分布式目录的部署。

<a id="watch-the-migration"></a>

## 监视迁移

将 Grain 调用错误率与延迟、激活实例变动及失败、重复激活报告、成员资格变动和目录注册错误与升级前基线进行比较。在 Silo 离开的同时观察目录快照传输、范围恢复及范围锁定持续时间。

混合阶段的临时重复激活实例应在升级完成后消解。最终健康检查点应具备以下条件：集群全部使用分布式目录，成员资格视图稳定，恢复完成，现有和新 Grain 的调用均成功，每个单激活实例 Grain 只有一个已注册的激活实例。

超过应用程序安全性或可用性阈值时，暂停替换。保留目录及成员资格日志，并维持足够的分布式目录容量。调查全集群分布式目录收敛后仍存在的重复激活实例或注册不匹配。

<a id="recovery-and-one-way-migration"></a>

## 恢复与单向迁移

集群一旦开始使用默认分布式目录，就应继续迁移至全部采用分布式目录的集群。不支持将该集群恢复为旧版目录。在引入首个默认使用分布式目录的 Silo 之前，应确定恢复过程并验证部署。

使用经过验证的恢复过程恢复应用程序状态，同时保留分布式目录及其分区配置。将兼容的应用程序映像、存储变更和恢复点纳入该计划。

<a id="upgrade-existing-distributed-directory-clusters"></a>

## 升级现有分布式目录集群

对现有分布式目录集群执行滚动运行时升级时，应同时保留分区数量及从哈希边界到分区编号的映射。目录请求和所有权转移快照均按这些编号分区寻址，因此所有 Silo 必须对其范围达成一致。

Orleans 10.1 每个 Silo 使用 30 个分区，并在按无符号值对各 Silo 的均匀哈希值排序后分配分区编号。Orleans 10.2 将默认数量改为 1，并在使用多个分区时按哈希生成顺序分配编号。

升级运行 Orleans 10.1 的分布式目录集群时，将较新 Silo 的 <xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo> 设置为 `30`，并配置应用程序定义的 <xref:Orleans.Configuration.GrainDirectoryOptions.GetPartitionBoundaries> 委托，按排序后的顺序返回各 Silo 的均匀哈希值：

:::code language="csharp" source="../snippets/compiled/Deployment/DirectoryPartitioningSnippet.cs" id="legacy_directory_partitions":::

对不可变哈希数组排序可保留 Silo 的缓存哈希顺序，并生成现有分区标识。升级后以及所有兼容的分布式目录恢复部署中，均应保留此配置。

已经使用 Orleans 10.2 及以后版本生成顺序映射的集群，应保留默认边界函数和现有分区数量。在持续流量下验证运行时升级和兼容的应用程序恢复，检查目录注册信息的保留及激活实例唯一性。更改现有集群的分区数量或边界映射需要协调的全集群重启，或对 Grain 状态所有权进行控制的蓝绿切换。

<a id="rebuild-uniform-hash-callers"></a>

## 重新构建均匀哈希 API 的调用方

<a id="uniform-silo-hash-api-compatibility"></a>

<xref:Orleans.Runtime.SiloAddress.GetUniformHashCodes*> 返回一个与 Silo 的缓存哈希存储共享数据的 <xref:System.Collections.Immutable.ImmutableArray`1>。重新构建针对 `uint[]` 返回类型编译的调用方。需要可变缓冲区时使用 `ToArray()`，或使用 `Sort()` 获取排序后的不可变结果，同时保留缓存值。

有关目录的当前配置和架构，请参阅[Grain 目录](../host/grain-directory.md)及[Grain 目录架构](../implementation/grain-directory.md)。
