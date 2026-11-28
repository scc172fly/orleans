<a id="live-application-durability-scenarios"></a>

# 实际应用持久性场景

这些测试运行普通的 `InProcessTestCluster` Silo，并将已有的
商品目录应用和外部效果处理方案编译到测试程序集中，使用生产实现中的
持久化任务、日志、收件箱、发件箱和作业处理程序。

每个测试类均使用 JSON 和 Orleans 二进制日志格式运行。每个测试
拥有自己的集群、已提交字节存储、控制手段及各个时钟。

<a id="contracts-and-oracles"></a>

## 契约与判定基准

| 测试方法 | 契约及判定依据 |
|---|---|
| `CommittedReservationIntentRecoversBusinessResultAfterSenderDeactivation` | 已提交的出站库存预留在发送方优雅停用后仍然存在。恢复后的订单成功，库存变化量恰好等于请求数量，并且重试保持相同的消息标识及发送时间。 |
| `LostAcceptanceReplyRedeliversSameReservationAfterReceiverRecovery` | 接收方在其回复丢失前提交接受记录。接收方恢复后，重新投递返回 `Duplicate`，且库存仅预留一次。允许发送方在重试接受请求之前完成：独立的完成消息可能已经确定结果。 |
| `CommittedReservationIntentRecoversBusinessResultAfterImmediateSiloLoss` | 出站预留在测试宿主的 Silo 瞬间丢失、发送方在不同 Silo 上恢复之后仍然存在，无需客户端重新提交。 |
| `CancellationAfterOneParallelReservationCompensatesOnlyTheSuccessfulLine` | 取消操作释放已完成的订单行，而非仍在等待验证的并行订单行。第一次释放返回 true，第二个释放任务不存在，两项库存余额均恢复到初始值。 |
| `EffectIntentAcknowledgementFollowsStorageNotStaging` | 在追加获准进入存储之前，独立恢复只发现已有意图，而非新意图。在存储确认之后、延续操作执行之前，恢复能发现新意图，即使提交操作还未返回。 |
| `PaymentAppliedWithLostResponseRetriesTheSameKeyAfterOwnerRecovery` | 提供程序执行一次付款。所有者恢复和重试复用提供程序键，使用不同的尝试 ID，并产生已授权的业务结果。显式调用尚未到期的任务不会派发。 |
| `InterruptedNonRepeatableEmailRecoversItsReconciliationOwnerWithoutRedispatch` | 提供程序提交电子邮件执行结果，随后所有者写入结果时在进入存储之前失败。恢复保留已提交的尝试标记及负责的操作人员。查询结果未知时，保留对账处理所有权和未来操作，而非重新派发。 |
| `ExhaustedRetryDecisionAndAuthorizedFollowUpSurviveImmediateSiloLoss` | 两次已证实未产生效果的尝试耗尽重试预算。终态失败及单独授权的后续操作在 Silo 瞬间丢失后仍保留各自的决策、不同的标识及关联。 |

业务预期独立于恢复实现：库存
计算、仅一条由提供程序执行的记录、稳定的逻辑键、不同的尝试，
以及显式终态或对账状态。只读投影使用生产编解码器将已提交的字节
重放为新对象，不读取实时激活实例的状态，
也不保留对其实例值的引用。

<a id="barriers-and-clocks"></a>

## 屏障与时钟

- `OutboxRetryScheduled` 表示**暂存**，而非提交。已注册的
  状态机见证组件捕获暂存的重试，并仅在该捕获已获存储确认之后，通过
  `OnWriteCompleted` 发出信号。
- 投递筛选器在接收方准入之前使操作失败，或者允许接收方
  返回 `Accepted`，然后丢弃该应答。其显式第二次应答
  屏障独立于订单完成状态观察重新投递。
- `ScenarioJournalGate` 在存储准入之前，或在确认之后且调用方延续执行之前，
  阻塞一次目标写入。提供程序的执行结果通过其
  独立写入日志的字节进行检查，此时
  提供程序的应答仍被阻塞。
- 库存预留重试推进已有测试夹具的**持久化作业时钟**。
  外部效果场景仅推进处理方案的**键控所有者时钟**，
  并显式调用其生产实现中的 `RunDueAsync` 处理程序。自动
  作业投递仍安排在未来；这些测试并不声称覆盖外部效果处理方案的
  调度器投递机制。
- 时钟遵循真实时间加受控偏移量。长达一小时的退避边界
  使偶然经过的毫秒数无关紧要。不通过睡眠或静默间隔
  判定完成。有限的挂钟等待通过阶段、
  实体、任务状态、活跃 Silo、时钟及最后观察到的屏障诊断停滞。

<a id="failure-model-limits"></a>

## 故障模型限制

激活实例的优雅停用、Silo 瞬间丢失、日志提供程序写入失败及外部应答丢失
是独立的场景。共享的易失性
提供程序在激活实例和 Silo 丢失后保留复制的序列化字节。
它不是持久存储设备：这些测试不证明操作系统进程终止、
断电、fsync 或文件系统重排序情况下的保证。
日志提供程序写入失败用于验证日志隔离及恢复，而非本地磁盘的
致命错误处理程序。

这些是实际应用场景，而非兼容性重放测试。
历史升级兼容性仍由现有的
历史记录重放测试套件负责验证。
