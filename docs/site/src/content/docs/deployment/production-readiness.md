---
title: "生产准备状态清单"
description: "在它收到生产量之前,审查 Orleans 部署情况。"
ms.date: 08/02/2026
ms.topic: checklist
---

<a id="production-readiness-checklist"></a>

# 生产准备状态清单

完成每个生产环境的这个核对表。 记录所有者、 预期值和连接到运行簿的链接, 而不是依赖隐含平台默认值 。

<a id="identity-and-compatibility"></a>

## 身份和兼容性

- [ ] 每个 silo 和客户都使用预定的 Orleans 套件版本。
- [ ] <xref:Orleans.Configuration.ClusterOptions.ServiceId> is stable for the lifetime of the application and isn't reused by an unrelated application.
- [ ] <xref:Orleans.Configuration.ClusterOptions.ClusterId> identifies this deployment environment. Production, staging, and blue/green clusters use distinct values unless they are intentionally joining the same cluster.
- [ ] Grain 接口、序列器和持续状态变化与选定的 [战略升级战略](upgrades.md)兼容。

<a id="networking-and-discovery"></a>

## 联网和发现

- [ ] 每个 silo 都刊登地址和 silo 港口广告,供其他各 silo 进入。
- [ ] 每个外部客户 Orleans 都可以到达广告上公布的网关地址和港口。
- [ ] 倾听端点与流程内可用的界面连接;广告端点描述同行如何达到此流程。
- [ ] 网络政策、防火墙、服务模具和网络地址翻译保持长期双向连接TCP。
- [ ] Silos 和客户使用同样的生产 [集群提供商和集群特性](networking.md#choose-a-clustering-provider)。
- [ ] 集群供应商数量众多,经过能力测试,在环境之间适当隔离。

<a id="state-and-dependencies"></a>

## 国家和依附关系

- [ ] 每个 grain 存储、提醒、流和集群供应商都明确配置生产。
- [ ] 必须在激活或集束损失中生存的数据使用耐久 grain 储存。在存储时仅用于可支配数据。
- [ ] 依赖者使用工作量身份或其他短期的证明机制。 秘密并不嵌入图像、来源或部署清单中。
- [ ] 超时、货币通货限额、断路器和 [约束的重试政策](handling-failures.md) 防止依赖性失灵成为再试风暴。
- [ ] 衰减或无法获取的抚养行为有文件记载:失灵封闭、拒绝工作、提供陈旧数据,或者缓冲一定数量的工作。

<a id="lifecycle-and-health"></a>

## 生命周期和健康

- [ ] 在 silo 加入该组群并使用所需的依赖关系之前,启动工作仍然无法进行。
- [ ] 准备状态在升空或关闭开始之前就被移除。
- [ ] 生命检查只检测出一个无法在当地取得进步的过程; 它们不会仅仅因为没有远程依赖性而重新启动进程。
- [ ] 平台的终止宽限期超过主机关闭超时和优雅所需的时间 silo 关闭。
- [ ] 滚动部署环境保持足够准备 silos ,以维持能力。

<a id="security-and-access"></a>

## 安全和出入

- [ ] [Orleans 信托边界](../security/index.md) 和应用程序所属安全控制都有记录。
- [ ] 只有可信赖的工作量才能到达 silo 和网关港口。
- [ ] Orleans 运输安全配置时,网络已不是一个可信赖的孤立边界。见 [Orleans 运输层安全](../host/transport-layer-security.md)。
- [ ] Grain 电话要求强制执行 [应用程序认证和授权](../security/authentication-authorization.md),经审定的证明书确认通过请求背景携带的身份。
- [ ] 序列质变器类型名称分辨率跟随 [最不给予特权的政策](../security/serialization.md)。
- [ ] 行政终点、健康细节、测量和日志 并不暴露秘密或租户数据
- [ ] 提供人的身份对成员资格、国家地位、提醒和流派都最没有特权。
- [ ] 证书和全权证书附有轮换和到期通知。

<a id="observability-and-operations"></a>

## 观察和运行

- [ ] 日志, Orleans 度, .NET 运行时间度和痕迹以中央出口。 [Orleans 易观测](../host/monitoring/index.md).
- [ ] 显示已备妥的 silo 计数、会籍变动、要求延时和故障、拒绝或投放信息、激活计数、CPU、记忆和依赖性健康。
- [ ] 警报是基于用户影响和持续症状,而不是个别的短暂会员活动。
- [ ] 操作员可以将部署版本、 silo 名称、集组识别码、服务身份和所有日志和遥测的主机实例联系起来。
- [ ] 命名所有人和运行手册跟随 [生产作业指南](production-operations.md) 失败的推出、部分网络分割、 供应商故障、超载、数据恢复和证书过期。

<a id="capacity-and-recovery"></a>

## 能力与恢复

- [ ] 装入测试包括预期交通量、连续、热 grain 键、 silo 损失和依赖性减速。
- [ ] 缩放政策使用测量饱和度,包括至少丢失一个失败域的上层。
- [ ] 持久应用数据被备份并进行恢复测试。
- [ ] 回收目标区分成员数据与 grain 状态、提醒数据、流州和应用数据库。
- [ ] 灾后恢复演习已经证明有文件记载 [恢复程序](disaster-recovery.md)。

在发射前,运行一个控制下的重新启动和一个在生产式负荷持续时的silo-时间尺度。 任何正确性都不应该取决于优雅的关闭成功,因为流程总是会突然失败。
