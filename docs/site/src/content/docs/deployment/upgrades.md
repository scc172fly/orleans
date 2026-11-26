---
title: "优优优停机和升级"
description: "使用优雅、滚动或蓝绿色战略,在 Orleans 应用程序中进行规模调整并部署应用。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="graceful-shutdown-and-upgrades"></a>

# 优优优停机和升级

Orleans 容忍突然的流程损失,但优雅的关闭会减少失败电话和避免不必要的恢复工作。 正确性决不能依赖优雅的关闭,因为一个进程、主机或网络可以毫无警告地失败。

<a id="graceful-shutdown-and-scale-in"></a>

## 优优异关闭和规模化

对于被删除的每个实例:

1. 停止接受新的应用程序流量, 报告尚未准备好 。
1. 停止接受新项目, 以接受与应用程序相关的特定背景工作 。
1. 问 .NET 等待等待 <xref:Microsoft.Extensions.Hosting.IHost.StopAsync*?displayProperty=nameWithType> 或正常的主机终止。
1. 允许 Orleans 离开集群成员,停止使用或转移运行时间责任。
1. 只有在关闭期限到期后终止该程序。

管弦乐队终止的宽限期必须超过主机关闭超时 .NET 。 测量负载和供养人延迟休假的间隔下的实际时间长度。

逐步缩放。 一次删除一个失败域, 等到会籍稳定、 继续 silos 吸收激活和延迟恢复正常。 不要将组群降低到测试的冗余或容量底线以下 。

有关主机API,见 [关闭 Orleans](../host/configuration-guide/shutting-down-orleans.md) 。

<a id="rolling-upgrades"></a>

## 滚式升级

使用滚动升级, 当新旧版本能够安全共享时:

- Grain 接口和序列式有效载荷。
- 状态 grain
- 提醒,流,和提供者的计划。
- 外部副作用和重复记录。
- 集群配置和运输环境。

推出前:

1. 升级客户端和 silos ,顺序与两个版本一致。
1. 首先使用添加剂合同更改; 不重复使用或重新编号序列器字段ID。
1. 使储存迁移与后向兼容,独立可逆。
1. 检查最低准备状态silo 计算和快速部署能力。
1. 测试回滚,同时两种版本和两种状态格式都存在。

一次替换 silos 的捆绑数。 当错误率、 延迟度、 会籍量、 依赖性负载或启动失败超过推出门槛时暂停 。

Orleans grain 版本可以在兼容的 grain 执行之间调用电话,但不能任意应用或储存更改。见 [部署 grains新版本](../grains/grain-versioning/deploying-new-versions-of-grains.md) 和 [后向兼容准则](../grains/grain-versioning/backward-compatibility-guidelines.md)。

<a id="release-specific-migration-guidance"></a>

### 特定排放的移徙指南

<a id="uniform-silo-hash-api-compatibility"></a>
<a id="distributed-directory-upgrade-compatibility"></a>

遵循遗留的切换指令程序 [Grain 目录迁移](../migration/grain-directory.md#legacy-to-distributed-directory-upgrade) 、分区兼容性 [现有分配式指令升级](../migration/grain-directory.md#upgrade-existing-distributed-directory-clusters) 和受影响呼叫者的 [统一 API 适应](../migration/grain-directory.md#uniform-silo-hash-api-compatibility) 。

<a id="blue-green-upgrades"></a>

## 蓝绿色升级改造

当版本无法在一个 Orleans 组中安全共存时使用蓝色绿色部署。

- 给以蓝色和绿色的 `ClusterId` 不同值。
- 决定它们是否可以共享 grain 存储。 共享只有在两个版本都能读写相同的记录而没有同时的所有权或不相容的副作用时才安全。
- 应用系统反向外部交通路线,而不是在 silo 端点前设置负载平衡器。
- 保持以前的环境,直到各州兼容性和倒退限制允许迁离为止。

对于有名的截断,请使用其中一项明确战略:

- Quiesce写道,移民状态,验证,然后交换通量。
- 通过申请所有移民协议和和解双面写字。
- 使用单独的国营仓库,进行离线转移。

从未在相同的变异 grain 状态发现两个互不兼容的活跃组群, 并假定 Orleans 成员将协调它们。 成员资格范围由集束标识决定; 它不会提供跨集群所有权 。

<a id="rollback"></a>

## 返回回滚

滚回计划必须包含代码、配置、系统图、证书和持续状态。 如果新版本已经写入数据,旧版本无法读取,只返回图像不是倒退。

当安全阈值超过时停止推出。 保存失败事件的日志和跟踪, 恢复已知兼容版本的能力, 避免重复自动重试加入该成员。
