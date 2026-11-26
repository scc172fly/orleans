---
title: "Orleans 移徙指南"
description: "计划并实施升级至 Orleans 10,从 Orleans 9、8、7或更早的释放。"
ms.date: 08/07/2026
ms.topic: how-to
ms.custom: migration-guide
---

<a id="orleans-migration-guides"></a>

# Orleans 移徙指南

使用与当前生产中使用的主要 Orleans 版本相匹配的指南。

| 启动版本 | 建议路径 | 指南指南指南指南 |
|------------------|------------------|-------|
| Orleans 9.x | 更新到最新的 9x 补丁, 然后升级到 Orleans 10. x | [升级 Orleans 9. x 到 10.x](migration/9-to-10.md) |
| Orleans 8.x | 更新至 Orleans 8.2,验证 Orleans 9.2,然后升级为 Orleans 10x | [升级 Orleans 8.x 到 10.x](migration/8-to-10.md) |
| Orleans 7.x | 更新最新7.2个补丁,然后验证每个得到支持的主要转换检查站 | [升级 Orleans 7.x 到 10.x](migration/7-to-10.md) |
| Orleans 3.x或更早 | 调整为 Orleans 7,在一个单独的组别中,然后按顺序连续进行 | [已存档 Orleans 3.x至 7.x 便笺](migration/3-to-7-archive.md) |

> [!IMPORTANT]
> Orleans 混合版本部署安全文件只在一个主要版本家庭内提供。 Orleans 9和9 Orleans 10 silos 在同一生产组别中,指南使用平行分组部署作为跨越主要版本的安全默认。

<a id="what-every-upgrade-must-preserve"></a>

## 每一次升级必须保存什么

在改变包件之前,记录以下兼容性合同:

- Orleans、 .NET、提供者和数据库版本目前部署。
- 下 grain 和每台 silo 客户。
- 稳定的序列质变器成员身份识别码和输入 grain 存储、流、提醒和队列信件中数据的别名。
- 集群、持久性、提醒、串流和 grain——目录提供者及其形式版本。
- 行为敏感选项的清晰值,包括请求取消、放置、 grain 目录缓存和计时器连接。
- 耐久状态和供应商元数据的备份或恢复点。

见 [升级部署和回调](migration/deployment-and-rollback.md) ,然后选择部署战略。

<a id="activation-metric-schema-update"></a>

## 动能指标系统的最新计划

**下一期发布说明:** 启动生命周期反转、延时直方图和人口计现在包括 `grain_type`,使用语气 `GrainId.Type.ToString()` 身份。 `orleans-grains` 从含有CLR执行名称的 `type` 键迁移到包含该语气特性的 `grain_type` 。因此,明确命名 grains 和构建通用 grains 可以有不同的数值和不同钥匙。

更新仪表板组合、过滤器、记录规则和 OpenTelemetry 视图以保留 `grain_type`。为了混合版本推出,使用排放服务版本选择旧的或新的系统。用部署 grain- 类型注册来绘制老的 CLR 名称,在合并序列之前先使用语法名称。将每个发射系列一次的合并浏览数。

激活仪表现在发布每个缓存类型(包括上次启动假后零位)的快照。 通过对每个 silo 和类型的最新观测进行总结,获取集群总数。通过导出来保存发射身份并在后端配置存储序列过期。生命周期计数器保留添加事件语义:在同一个间隔内按每类计算其增加量,恢复了总的事件量。

关闭计数器保留了每个非空收集批次的附加历史事件,只有 `via=collection`;单个关闭事件带有激活的卡方型。在汇总全部关闭事件时包括打字和未打字的捐款。采集扫描计数器保留扫描水平测量数据。 `orleans-system-targets` 保留其 `type` 键,管理统计 grain 保留每silo 账户显示的CLR名称。

单位、生命周期结果、人口范围和汇总见 [计量表](host/monitoring/metrics-catalog.md#grain-type-identity-and-aggregation) 。

<a id="package-version-policy"></a>

## 套件版政策

将所有 `Microsoft.Orleans.*` 软件包保存在同一 10.x 补丁上。 对于使用 NuGet 中央软件包管理软件的解决方案, 请在 `Directory.Packages.props` 中声明版本, 从项目级别 `PackageReference` 项中删除版本。 不要从迁移示例复制旧供应商依赖性版本; 选择一个得到目标运行时间和提供者支持的当前版本 。

有关指导见:

- [Orleans 序号](host/configuration-guide/serialization.md)
- [部署 grains新版本](grains/grain-versioning/deploying-new-versions-of-grains.md)
- [ADO.NET 供应商配置](host/configuration-guide/configuring-ado-dot-net-providers.md)
- [排除部署故障](deployment/troubleshooting-deployments.md)
