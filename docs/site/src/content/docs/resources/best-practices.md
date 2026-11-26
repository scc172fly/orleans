---
title: "Orleans 最佳做法"
description: "设计、实施和有效操作 Orleans 应用程序。"
ms.date: 08/02/2026
ms.topic: best-practice
---

<a id="orleans-best-practices"></a>

# Orleans 最佳做法

Orleans 在应用模型中工作最有效,因为许多独立可处理的实体的状态和工作可以按关键部分划分。这些做法是起点;衡量代表工作量并调整设计以适应应用程序领域。

<a id="model-grains-around-domain-ownership"></a>

## 围绕域所有权的模型 grains

- 给每个 grain 明确的身份和责任,如用户、账户、设备、订单、房间或会话。
- 保留在可行时必须在同一 grain 内同时变化的变量。
- 避免使用小 grains 网络,这些网络多次打电话来完成一次作业。
- 避免在高通量路径上使用单子调试器 grains 。通过键盘或使用分阶段汇总进行分区协调。
- 不要假设 grain 引用意味着地点。 grain 呼叫可以跨过进程或机器。

普通状态 grain 在组群中有一个激活,除非其位置或登记另有其他说法。 Orleans 并不自动生成复制物来增加吞吐量。使用分割身份、无国籍工人、在应用层设计的阅读复制品或其他适当模式。

<a id="keep-grain-turns-short-and-asynchronous"></a>

## 保持 grain 短小和无同步

- 不要用 `.Wait()`、 `.Result`、同步 I/O、睡眠或锁来阻断线条。
- [等待](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/await) I/O和 grain 调用。
- 切换到 grain 转弯时避免CPU- 密集循环。将大量平行计算移到合适的工人或计算服务中。
- 而不是制造数量无限制的通话。
- 合同支持取消的吊销牌,将吊销作为合作性处理。

Grain 激活进程默认时一个转一个转。 Reentrant grains 和呼叫链中继器可以互换, 所以不能仅仅让他们避免呼叫周期问题。 首先简化调用图; 如果插入是故意的, 则以文件记录并测试跨越 `await` 点的变量 。

<a id="design-grain-apis-for-a-network"></a>

## 网络设计 grain API

- 更倾向于粗皮业务,这些业务表明对动产式产权证的域意图。
- 保持合理的参数和返回值大小。
- 将每个 grain 调用都视为不可置信。 调用会籍变更、程序故障、超载、序列化或应用例外,可以暂停或失败。
- 设计围绕应急能力重新进行。 Orleans 提供最接近于默认情况下发送电文,而不是完全处理。
- 避免在多个层次内无限期地重试。 在适当的边界上建立有约束、可观察的重试政策 。

<a id="persist-state-deliberately"></a>

## 蓄意的持久性状态

使用 <xref:Orleans.Runtime.IPersistentState`1> 或其他支持的国家模式,并选择适合耐久性、一致性、延缓性和操作要求的提供者。

- 更改一个模拟状态对象不会自动写入。 调用持久性 API , 等待它报告在耐久性是业务合同的一部分时成功与否。
- 处理存储失败 。 错误的写法表示没有达到要求的耐久性 。
- 保持序列类型版本的耐受性。使用 <xref:Orleans.GenerateSerializerAttribute> 和稳定 <xref:Orleans.IdAttribute> 值;不重复使用或重编现有字段编号。
- 保持小到能满足供应商项目和请求限制的状态物体大小。
- 国家必须生存过程或 silo 损失时, 不要使用存储存储。 它会将记录分布在集束存储 grains 中, 但无法持久复制它们 。
- 不要依赖 silo本地文件系统作为共享生产储存。

持久性并不复制激活的模拟状态。 进程失败后恢复取决于是否已经写给一个耐用且可用的供应商。

<a id="choose-production-providers-as-a-set"></a>

## 选择生产供应商作为集

多个silo 部署需要共有的组群成员。 大多数生产应用程序也需要对 grain 储存、提醒、流、持久工作以及 grain 目录进行审慎选择。

- 保持 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 稳定性,用于应用程序,并使用 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 来区分不得相互结合的部署。
- 使用单独的部署身份来发展、测试、中转和生产。
- 遵循供应商的认证、加密、备份、能力和高可用性指导。
- 测试提供者在抽打、短暂故障和区域断电。
- 不要直接将 silo 或网关端点暴露给不信任的公共客户。在集群前设置一个认证的应用协议,如 HTTPS 。

当地发展需要本地主机群集和内线供应商的方便性。它们并不模拟所有故障模式或生产基础设施的保障。

<a id="plan-for-membership-changes"></a>

## 成员变更计划

Silos 可以随时加入、离开、重新开始或失败。

- 运行足够 silos 和基础设施复制,以满足应用程序的可用目标。
- 期望在 silo 失败时,飞行中的呼叫会失效。成员聚集后,以后的呼叫可以恢复健康 grains 的 silos。
- 使用滚动部署和 grain 格式化功能,当合同或执行发生变更时使用。
- 了解配置的安置策略。 激活再平衡可以改善分配, 但不会删除热键或取代能力规划 。
- 尽可能的优雅关闭,但也要确认突然的流程损失。

<a id="observe-the-application"></a>

## 观察应用程序

使用标准 [.NET 记录](https://learn.microsoft.com/dotnet/core/extensions/logging)、 [度数](https://learn.microsoft.com/dotnet/core/diagnostics/metrics)和 [分布追踪跟踪](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing),并考虑采用 Orleans 板进行集束检查。

- 在日志中包含 grain 类型和安全识别符号,而不记录机密或敏感状态。
- 监视器呼叫延迟、超时、拒绝请求、排队长度、启动计分、记忆、CPU、垃圾收集、成员变更和提供者健康。
- 提醒用户注意其明显症状以及基础设施信号。
- 保持日志水平通过 .NET 配置系统可配置。
- 使用适合东道国环境的卫生和准备状态检查。

<a id="test-at-the-right-levels"></a>

## 在正确的水平上进行测试

- 尽可能独立测试单位域逻辑。
- 使用 `Microsoft.Orleans.TestingHost` 需要进行试验的 grain 激活、序列化、时间安排或多silo 行为。
- 测试信件序列兼容性和持续状态。
- 增加对申请所用生产提供者类型的整合测试。
- 工作重试、重复请求、超时、流程终止、滚动升级和回收。
- 统一合成钥匙可以隐藏热grain 问题。

<a id="review-production-readiness"></a>

## 审查生产准备状态

在部署之前,确认申请:

- 必要时共有成员和持久提供者。
- 稳定的集群和服务身份。
- 认证和网络边界。
- 断层重整和无能操作
- 容量限制,后压和超载行为。
- 日志、测量仪、跟踪、健康检查和警报。
- 备份、恢复、升级和回退程序。
- 测试选定基础设施可能发生的故障。

具体供应商的软件包见 [Orleans NuGet 包](nuget-packages.md)。版本升级,使用 [移徙指南](../migration-guide.md)。
