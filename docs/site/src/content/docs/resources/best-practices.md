---
title: Orleans 最佳实践
description: 有效地设计、实现和运营 Orleans 应用。
ms.date: 08/02/2026
ms.topic: best-practice
---

<a id="orleans-best-practices"></a>

# Orleans 最佳实践

当应用对大量可独立寻址的实体建模，且其状态和工作可按键划分时，Orleans 最能发挥优势。以下实践是设计的起点；应测量具有代表性的工作负载，并根据应用领域调整设计。

<a id="model-grains-around-domain-ownership"></a>

## 围绕领域所有权对 Grain 建模

- 为每个 Grain 明确标识与职责，例如用户、账户、设备、订单、房间或会话。
- 在可行的情况下，将必须一起变更的不变量放在同一个 Grain 中。
- 避免创建由大量细粒度 Grain 组成、完成一次操作需要多次调用的网络。
- 避免在高吞吐量路径中使用单例协调 Grain。按键划分协调工作，或采用分级聚合。
- 不要认为 Grain 引用意味着本地性。Grain 调用可能跨进程或计算机。

普通有状态 Grain 在集群中只有一个激活实例，除非其放置或注册方式另有规定。Orleans 不会自动为其创建副本来提高吞吐量。应采用分区标识、无状态工作者、应用层设计的读取副本或其他合适的模式。

<a id="keep-grain-turns-short-and-asynchronous"></a>

## 保持 Grain 执行轮次简短且异步

- 不要通过 `.Wait()`、`.Result`、同步 I/O、休眠或锁阻塞线程。
- 使用 [await](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/await) 等待 I/O 和 Grain 调用。
- 避免在 Grain 执行轮次中执行 CPU 密集型循环。将大量并行计算移交给适当的工作者或计算服务。
- 限制扇出和并发度，不要创建数量无界的调用。
- 在契约支持取消时传递取消令牌，并将取消视为协作式操作。

默认情况下，Grain 激活实例一次只处理一个执行轮次。可重入 Grain 和调用链可重入性允许执行轮次交错，因此不要仅仅为了规避循环调用问题就启用它们。先简化调用图；如果有意允许交错执行，应记录并测试跨 `await` 点的不变量。

<a id="design-grain-apis-for-a-network"></a>

## 面向网络设计 Grain API

- 优先使用表达领域意图的粗粒度操作，而非需要频繁往返调用的属性式 API。
- 将参数及返回值的大小控制在合理范围内。
- 将每次 Grain 调用视为可能失败。成员资格变化、进程故障、过载、序列化或应用异常都可能导致超时或失败。
- 围绕幂等性设计重试。Orleans 默认提供至多一次消息投递，而非恰好一次处理。
- 避免多个层级分别无限重试。在适当边界制定有界且可观测的重试策略。

<a id="persist-state-deliberately"></a>

## 有意识地持久化状态

使用 <xref:Orleans.Runtime.IPersistentState`1> 或其他受支持的状态模型，并根据持久性、一致性、延迟和运维要求选择合适的提供程序。

- 修改内存中的状态对象不会自动将其写入存储。如果持久性是操作契约的一部分，应调用持久化 API，等待其完成后再报告成功。
- 显式处理存储故障。写入失败意味着所要求的持久性尚未得到保证。
- 保持序列化类型的版本容错能力。使用 <xref:Orleans.GenerateSerializerAttribute> 和稳定的 <xref:Orleans.IdAttribute> 值，不要复用或重新编号已有字段 ID。
- 控制状态对象大小，使其符合提供程序的单项大小及请求大小限制。
- 如果状态必须在进程或 Silo 丢失后保留，不要使用内存存储。它将记录分布到集群存储 Grain 中，但不会持久化复制这些记录。
- 不要依赖 Silo 的本地文件系统作为生产环境的共享存储。

持久化不会复制激活实例的内存状态。进程故障后的恢复依赖于此前已将状态写入可用的持久化提供程序。

<a id="choose-production-providers-as-a-set"></a>

## 成套选择生产环境提供程序

多 Silo 部署需要共享的集群成员资格。大多数生产应用还需针对 Grain 存储、提醒器、流、持久化作业和 Grain 目录作出明确选择。

- 保持应用的 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 稳定，并使用 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 区分不可互相加入的部署。
- 为开发、测试、预发布和生产环境使用不同的部署标识。
- 遵循提供程序的身份验证、加密、备份、容量和高可用性指南。
- 测试提供程序限流、暂时性故障和区域中断。
- 不要将 Silo 或网关终结点直接暴露给不可信的公共客户端。在集群前设置经过身份验证的应用协议，例如 HTTPS。

本地开发使用本地主机集群和内存提供程序较为方便，但它们不能模拟生产基础设施的所有故障模式或保证。

<a id="plan-for-membership-changes"></a>

## 为成员资格变化做好准备

Silo 随时可能加入、离开、重启或发生故障。

- 运行足够数量的 Silo 和基础设施副本，以满足应用的可用性目标。
- Silo 故障时，预计正在进行的调用可能失败。成员资格收敛后，后续调用可在健康的 Silo 上重新激活 Grain。
- 契约或实现发生变化时，使用滚动部署和 Grain 版本控制功能。
- 理解已配置的放置策略。激活实例再均衡能够改善分布，但不能消除热点键或替代容量规划。
- 尽可能采用优雅关闭，同时也要验证进程突然丢失的情况。

<a id="observe-the-application"></a>

## 观测应用

使用标准的 [.NET 日志记录](https://learn.microsoft.com/dotnet/core/extensions/logging)、[指标](https://learn.microsoft.com/dotnet/core/diagnostics/metrics) 和[分布式跟踪](https://learn.microsoft.com/dotnet/core/diagnostics/distributed-tracing)，并考虑使用 Orleans Dashboard 检查集群。

- 在日志中包含 Grain 类型及安全的标识符，不要记录机密或敏感状态。
- 监控调用延迟、超时、被拒绝的请求、队列长度、激活实例数量、内存、CPU、垃圾回收、成员资格变化和提供程序健康状况。
- 对用户可感知的症状及基础设施信号均设置告警。
- 通过 .NET 配置系统保持日志级别可配置。
- 使用适合宿主环境的健康检查与就绪检查。

<a id="test-at-the-right-levels"></a>

## 在适当层级进行测试

- 尽可能独立对领域逻辑进行单元测试。
- 对需要 Grain 激活、序列化、调度或多 Silo 行为的测试使用 `Microsoft.Orleans.TestingHost`。
- 测试消息和持久化状态的序列化兼容性。
- 针对应用在生产环境中使用的提供程序类型添加集成测试。
- 验证重试、重复请求、超时、进程终止、滚动升级和恢复行为。
- 对具有代表性的键分布进行负载测试。均匀分布的合成测试键可能掩盖热点 Grain 问题。

<a id="review-production-readiness"></a>

## 检查生产就绪情况

部署前，确认应用具备以下条件：

- 在需要的地方使用共享成员资格和持久化提供程序。
- 稳定的集群和服务标识。
- 身份验证与网络边界。
- 有界重试和幂等操作。
- 容量限制、背压和过载处理行为。
- 日志、指标、跟踪、健康检查和告警。
- 备份、还原、升级和回滚流程。
- 针对所选基础设施中可能发生的故障进行测试。

有关具体提供程序包，请参阅 [Orleans NuGet 包](nuget-packages.md)。有关版本升级，请参阅[迁移指南](../migration-guide.md)。
