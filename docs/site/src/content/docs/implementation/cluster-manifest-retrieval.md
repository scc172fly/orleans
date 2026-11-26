---
title: "群集货单检索"
description: "Orleans 如何发现 silo 的货单、重新使用内容的杂乱,以及在成员变更期间修复缺失元数据。"
ms.date: 09/10/2026
ms.topic: concept-article
---

<a id="cluster-manifest-retrieval"></a>

# 群集货单检索

每个 silo 都公布一个不可更改的 grain 状态表,描述其 grain 类型、界面和属性。集束状态数据提供商将这些状态表集合到用于类型分辨率和版本识别位置的元数据中。

<a id="membership-and-publication"></a>

## 成员和出版物

组群名单有一个与成员版本相对应的主要版本和一个微小版本,随着成员预购而提前。 silos 包括当地 silo 当它活动时, 局部 grain 本地元数据 silo 开始。

提供商检索缺失的运行状态 silos列表并公布成功结果。 在5秒后或当新会员快照到达时,将重新尝试不成功的获取。取消会达到远程呼叫并停止本地等候 。

<a id="content-addressed-retrieval"></a>

## 内容处理检索

<xref:Orleans.Configuration.ClusterManifestOptions.EnableContentAddressedRetrieval> defaults to `true`, enabling hash-based retrieval and peer repair. Set it to `false` in <xref:Orleans.Configuration.ClusterManifestOptions> to request each missing active silo's manifest directly. Configure the option through the silo builder before starting the silo. The provider captures the value at construction; a restart applies configuration changes.

启用此选项后, 提供者会询问每个缺失的 silo 本地货单内容。 当地大麻缓存中的匹配条目立即供应该货物。 否则, 提供者会要求用散列来填报货物清单, 核实其内容, 并添加到缓存中 。

Hashes 在固定的 Canonical Transaural 上使用递增 SHA-256 : 一个编码版本, 排序 grain 条目, 然后对界面条目进行分类。 每个区块和属性收藏从它的数开始。 标识符有一个字节前缀; 属性键和值有 UTF-16 代码- 单词前缀。 `-1` 的长度代表空值或默认识别符号, 而 `0` 则代表一个空值。 数字和长度签名为32比大半个大整数, UTF-16 代码单位是按大端顺序写成的。 此布局保存精确的识别符号和字符串内容, 包括无效的 UTF-16, 并给予相同的内容, 无论字典插入顺序如何, 都一样。 无法变形的明显例子的符号用弱密钥来回忆, 允许在所有人释放时收集这些清单 。

每个出版物都创建了一个新的缓存, 包含其真实的列表和本地元数据。 在飞行中检索保留了原始缓存实例。 一个老化的回收可以嵌入这个例子, 而更新的出版物则保留自己的缓存。 提供商在新缓存之前先披露新的缓存清单版本 。 检索器在检查版本前会捕捉缓存, 因此旧的更新也显示其成员快照已被取代 。

<a id="peer-repair-and-bounded-waiting"></a>

## 同伴修理和捆绑等候

当一个以上的活动 silo列表缺失时, 启用的提供者还会从轮换的有命令的成员名单中最多探测到三个同行。 直接检索开始与这些探测器同时进行 。

每个探测器都有其散装摘要和显示日期请求所共有的第二个截止期限。 混凝土可以直接满足缺失条目; 否则, 供应商会要求同行提供完整更新信息, 并对照摘要的散列对每个候选人进行核实。 成功修复的条目会立即公布, 包括部分维修。 这些条目的直接请求会被从规定的完成数据集中删除, 让其余的成功提取能够独立推进单件。

当直接检索首先提供所有缺失的单项时, 供应商取消选择的同行尝试并立即公布直接结果。 因此成功的直接路径将不考虑缓慢的同行总结请求而完成 。

供应商最多承认了三次同时进行本地检测的尝试。 完成、超时和取消呼叫立即释放尝试的空档, 允许稍后重试进行。 第二次截止时间取消了请求标记并结束了当地等待; Orleans 使用普通 RPC 路径向同行发送取消信号。 直接检索会继续同时进行这些尝试。 延迟回复不会改变完成的尝试结果, 并观察和记录晚期失败。 重试选择会通过活动同行旋转, 以五秒的延迟或更新的成员快照的速度进行回溯。

<a id="compatibility-rollout-and-rollback"></a>

## 兼容性、推出和反弹

Silos 应要求服务散列请求,包括当这些请求被配置用于直接检索时。使用默认内容处理模式的供应商在出现数据时计算和再利用内容的散列。

在滚动部署期间,启用了 silos 尝试散列检索,当同行拒绝新方法或其散列请求失败时,可以返回到已确立的直接 RPC。无效或无法使用的散列处理机也可以触发直接检索。独立远程取消遵循兼容路径;取消本地请求向呼叫者传播。同侪修复失败让直接检索负责填充缺失条目。

升级后 silos 在启动时采用内容处理检索,而现有的 silos 则继续使用其部署的实施工作。无支持的散列请求可在升级期间产生临时例外日志和额外请求;成功的直接回调提供所需的元数据。单个 silo- 最典型RPC保留配置的系统响应超时,而可选择的同行摘要/更新尝试有一秒钟的期限。

在组合和重新启动期间观察 Excell 检索和对等点诊断。 调试日志区分散列回落、 同行超时、 占用的探测槽和延迟失败。 警告会识别直接获取失败的失败。 要选择直接检索, 请将选项设置为 `false` 并重新启动受影响的 silos; 它们继续应请求响应被允许的同侪的散列要求 。

<a id="measuring-retrieval"></a>

## 衡量检索

`Microsoft.Orleans` 仪表显示 [代表表检索仪器](../host/monitoring/metrics-catalog.md#cluster-manifests)。在等同的组合和重新启动期间,将金丝雀与使用直接检索的 silos 比较:

| 信号信号 | 口译口译 口译口译口译口译口译口译口译口译口译口译口译口译口译口译口译口译 |
|---|---|
| `orleans-manifest-cache-lookups`,除以 `result` 和 `source` | 点击分数显示已知内容经常满足直接的散列要求或同侪摘要调查。 |
| `orleans-manifest-fallbacks`,除以 `reason` | 计及在错误、遗漏的体块或内容错配后通过直接 RPC 进行的散列检索。 |
| `orleans-manifest-peer-probes`,除以 `status` | 取消还包括由成功直接检索取代的选择性探测。 |
| `orleans-manifest-peer-repairs` | 由成功公布的同行维修提供的缺失条目 silo 。重复摘要为每份出版物提供一次修复条目。 |
| `orleans-manifest-retrieval-duration`,除以 `mode` 和 `status` | 测量每个本地 silo- 最显著的检索,以毫秒计,包括缓存搜索、后退和终端取消或故障。 |

属性使用固定类别,使时间序列基本内容在整个 silo 重新启动中保持稳定。直接模式记录检索期限,而散列和对等柜台则反映内容处理的检索活动。这些衡量尺度与序列式信件大小和 silo CPU/模件一起用于评价服务软件的显示尺寸和应用程序版本组合的好处。

成员转换见 [分组成员](cluster-management.md) ,数据元数据如何驱动接口转换选择见 [滚动版本 skew](rolling-version-skew.md) 。
