---
title: Orleans 迁移指南
description: 规划并执行从 Orleans 9、8、7 或更早版本升级到 Orleans 10 的过程。
ms.date: 08/07/2026
ms.topic: how-to
ms.custom: migration-guide
---

<a id="orleans-migration-guides"></a>

# Orleans 迁移指南

请选择与当前生产环境部署的 Orleans 主版本对应的指南。

| 起始版本 | 建议升级路径 | 指南 |
|------------------|------------------|-------|
| Orleans 9.x | 更新至最新 9.x 补丁版本，再升级至 Orleans 10.x | [将 Orleans 9.x 升级至 10.x](migration/9-to-10.md) |
| Orleans 8.x | 更新至 Orleans 8.2，验证 Orleans 9.2，再升级至 Orleans 10.x | [将 Orleans 8.x 升级至 10.x](migration/8-to-10.md) |
| Orleans 7.x | 更新至最新 7.2 补丁版本，再逐一验证各受支持的主版本检查点 | [将 Orleans 7.x 升级至 10.x](migration/7-to-10.md) |
| Orleans 3.x 或更早版本 | 在独立集群中迁移至 Orleans 7，再按顺序继续升级 | [归档的 Orleans 3.x 至 7.x 迁移说明](migration/3-to-7-archive.md) |

> [!IMPORTANT]
> Orleans 仅对同一主版本系列内的混合版本部署安全性提供文档说明。在完成自己的兼容性验证之前，不要将 Orleans 9 和 Orleans 10 的 Silo 部署在同一个生产集群中。本系列指南以并行集群部署作为跨主版本升级的安全默认方案。

<a id="what-every-upgrade-must-preserve"></a>

## 每次升级都必须保留的内容

更改软件包之前，请记录以下兼容性契约：

- 当前部署的 Orleans、.NET、提供程序及数据库版本。
- 每个 Silo 和客户端所使用的 Grain 接口及序列化类型程序集。
- Grain 存储、流、提醒器及排队消息中的数据所使用的稳定序列化成员 ID 和类型别名。
- 集群、持久化、提醒器、流及 Grain 目录提供程序和它们的架构版本。
- 对行为敏感的选项的显式值，包括请求取消、放置、Grain 目录缓存及定时器交错执行。
- 持久化状态和提供程序元数据的备份或恢复点。

选择部署策略前，请参阅[升级部署与回滚](migration/deployment-and-rollback.md)。

<a id="activation-metric-schema-update"></a>

## 激活实例指标架构更新

**下一版本的发行说明：** 激活实例生命周期计数器、延迟直方图及数量仪表现在包含 `grain_type`，使用规范的 `GrainId.Type.ToString()` 标识。`orleans-grains` 从包含 CLR 实现名称的 `type` 键迁移到包含规范标识的 `grain_type` 键。因此，显式命名的 Grain 和构造后的泛型 Grain 不仅键名不同，其值也可能不同。

更新仪表板分组、筛选器、记录规则和 OpenTelemetry 视图以保留 `grain_type`。对于混合版本发布，使用指标发出方的服务版本选择旧架构或新架构。合并时间序列前，通过部署中的 Grain 类型注册将旧 CLR 名称映射为规范名称。在合并视图中，每个发出方的时间序列只计数一次。

激活实例数量仪表现在为每个缓存的类型发出快照，包括最后一个激活实例离开后的零值。通过对每个 Silo 和类型的最新有效观测值求和，获取集群总量。导出指标时保留发出方标识，并在后端配置陈旧时间序列过期机制。生命周期计数器仍具有可加的事件语义：对同一时间区间内各类型的增量求和即可得到事件总量。

关闭计数器保留了其历史行为：每个非空回收批次额外产生一个仅带 `via=collection` 的事件；单个关闭事件带有相应激活实例的规范类型。汇总关闭事件总量时，同时计入带类型和不带类型的贡献。回收扫描计数器保留扫描级度量。`orleans-system-targets` 保留其 `type` 键，管理 Grain 的统计信息保留 CLR 显示名称，并按 Silo 统计。

有关单位、生命周期结果、数量统计范围及聚合方式，请参阅[指标目录](host/monitoring/metrics-catalog.md#grain-type-identity-and-aggregation)。

<a id="package-version-policy"></a>

## 软件包版本策略

让所有 `Microsoft.Orleans.*` 软件包保持在同一 10.x 补丁版本。对于采用 NuGet 中央软件包管理的解决方案，在 `Directory.Packages.props` 中声明版本，并省略项目级 `PackageReference` 项中的版本。不要照搬迁移示例中的旧提供程序依赖版本；应选择目标运行时与提供程序均支持的当前版本。

另请参阅：

- [Orleans 序列化](host/configuration-guide/serialization.md)
- [部署新版本的 Grain](grains/grain-versioning/deploying-new-versions-of-grains.md)
- [ADO.NET 提供程序配置](host/configuration-guide/configuring-ado-dot-net-providers.md)
- [部署故障排查](deployment/troubleshooting-deployments.md)
