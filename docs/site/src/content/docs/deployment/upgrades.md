---
title: 优雅关闭与升级
description: 使用优雅关闭、滚动升级或蓝绿部署策略缩减 Orleans 应用程序的规模及进行部署。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="graceful-shutdown-and-upgrades"></a>

# 优雅关闭与升级

Orleans 能够容忍进程突然退出，但优雅关闭可以减少调用失败并避免不必要的恢复工作。系统的正确性绝不能依赖优雅关闭，因为进程、主机或网络可能毫无预警地发生故障。

<a id="graceful-shutdown-and-scale-in"></a>

## 优雅关闭与缩容

对于每个将被移除的实例：

1. 停止接收新的应用程序流量，并报告实例未就绪。
1. 停止应用程序专用的后台工作接收新的工作项。
1. 请求 .NET 主机停止，并等待 <xref:Microsoft.Extensions.Hosting.IHost.StopAsync*?displayProperty=nameWithType> 完成或主机正常终止。
1. 允许 Orleans 退出集群成员资格，并停用激活实例或转移运行时职责。
1. 仅在关闭期限到期后才终止进程。

编排器的终止宽限期必须长于 .NET 主机的关闭超时时间。在负载下测量实际关闭耗时，并为提供程序的延迟留出余量。

逐步缩容。每次移除一个故障域，等待成员资格稳定、剩余 Silo 接纳激活实例且延迟恢复正常后，再继续操作。不要将集群缩减到低于经过测试的冗余度或容量下限。

有关主机 API，请参阅[关闭 Orleans](../host/configuration-guide/shutting-down-orleans.md)。

<a id="rolling-upgrades"></a>

## 滚动升级

当新旧版本可以安全地共享以下内容时，使用滚动升级：

- Grain 接口及序列化后的有效载荷。
- 已持久化的 Grain 状态。
- 提醒器、流和提供程序的架构（schema）。
- 外部副作用及去重记录。
- 集群配置及传输设置。

在推出新版本之前：

1. 按新旧版本都兼容的顺序升级客户端和 Silo。
1. 优先采用仅添加内容的契约变更；不要重用或重新编号序列化器字段 ID。
1. 确保存储迁移向后兼容，并且可以独立回退。
1. 验证就绪 Silo 数量的下限和升级期间的额外容量。
1. 在新旧版本及两种状态格式同时存在的情况下测试回滚。

每次只替换有限数量的 Silo。当错误率、延迟、成员资格频繁变动、依赖项负载或激活失败超过发布阈值时，暂停升级。

Orleans 的 Grain 版本管理功能可以在兼容的 Grain 实现之间路由调用，但并不能使任意应用程序变更或存储变更都具备兼容性。请参阅[部署新版 Grain](../grains/grain-versioning/deploying-new-versions-of-grains.md)及[向后兼容性指南](../grains/grain-versioning/backward-compatibility-guidelines.md)。

<a id="release-specific-migration-guidance"></a>

### 特定版本的迁移指南

<a id="uniform-silo-hash-api-compatibility"></a>
<a id="distributed-directory-upgrade-compatibility"></a>

按照 [Grain 目录迁移](../migration/grain-directory.md#legacy-to-distributed-directory-upgrade)中的步骤从旧版目录切换；参阅[升级现有分布式目录集群](../migration/grain-directory.md#upgrade-existing-distributed-directory-clusters)以确保分区兼容性；受影响的调用方应参照[均匀哈希 API 适配](../migration/grain-directory.md#uniform-silo-hash-api-compatibility)进行调整。

<a id="blue-green-upgrades"></a>

## 蓝绿升级

当不同版本无法在同一个 Orleans 集群内安全共存时，使用蓝绿部署。

- 为蓝色和绿色环境分配不同的 `ClusterId` 值。
- 确定两者能否共享 Grain 存储。只有当两个版本都能读写同一批记录且不会同时拥有同一状态，也不会产生不兼容的副作用时，共享存储才是安全的。
- 在应用程序入口路由外部流量，而不是在 Silo 终结点前部署负载均衡器。
- 保留原环境，直至状态兼容性及回滚约束允许将其移除。

对于有状态的切换，明确采用下列策略之一：

- 暂停写入、迁移状态、验证，然后切换流量。
- 使用由应用程序管理的迁移协议进行双写，并对数据进行协调核对。
- 使用独立的状态存储并执行离线数据迁移。

切勿将两个互不兼容的活动集群指向同一份可变的 Grain 状态，并假定 Orleans 成员资格机制会协调它们。成员资格以集群 ID 为作用域，并不提供跨集群的所有权管理。

<a id="rollback"></a>

## 回滚

回滚计划必须涵盖代码、配置、架构（schema）、凭据及已持久化状态。如果新版本已经写入旧版本无法读取的数据，那么仅还原容器镜像并不构成完整的回滚。

当超出安全阈值时，停止推出新版本。保留故障实例的日志和跟踪信息，使用已知兼容的版本恢复容量，并避免反复自动重试而造成成员资格频繁变动。
