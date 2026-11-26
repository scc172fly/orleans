<a id="live-application-durability-scenarios"></a>

# 实时应用耐久性假想

这些测试运行普通 `InProcessTestCluster` silos。它们汇编了现有的
使用
生产耐久的任务、期刊、收件箱、发件箱和工作处理员。

每个类以 JSON 和 Orleans 双进制日记格式同时运行。
拥有它的组群, 承诺字节存储、控制和时钟。

<a id="contracts-and-oracles"></a>

## 合同和手工艺

| 测试方法 | 合同和确定证据 |
|---|---|
| `CommittedReservationIntentRecoversBusinessResultAfterSenderDeactivation` | 承诺的退出保留在优雅发送者解除功能后仍能维持。 收回的订单成功,库存量完全按照要求的数量移动,重试保留了信息身份并发送时间。 |
| `LostAcceptanceReplyRedeliversSameReservationAfterReceiverRecovery` | 接收者在回复丢失前承诺接受。 接收者恢复后, 重新交付返回 `Duplicate` 并保留库存一次。 在接收之前允许发件人完成接收, 重试: 其单独的完成信件已经可以确定结果 。 |
| `CommittedReservationIntentRecoversBusinessResultAfterImmediateSiloLoss` | 离岸保留区在测试主机立即 silo 损失和发件人从不同的 silo收回后仍能幸存,客户无法重新提交。 |
| `CancellationAfterOneParallelReservationCompensatesOnlyTheSuccessfulLine` | 取消释放了完成的线,而不是平行线,该线仍在等待验证。 第一个释放返回是真实的,第二个释放任务不存在,两个库存余额都回到最初的价值。 |
| `EffectIntentAcknowledgementFollowsStorageNotStaging` | 在加入附件之前,独立追偿只找到现有意图,而不是新的意图。在确认储存后,但在继续之前,虽然提交书没有答复,但发现新意图。 |
| `PaymentAppliedWithLostResponseRetriesTheSameKeyAfterOwnerRecovery` | 供应商只支付一笔付款。 所有者收回和重新使用供应商的钥匙,使用不同的尝试身份,并产生授权业务结果。 明确但并非逾期的援引并不发出。 |
| `InterruptedNonRepeatableEmailRecoversItsReconciliationOwnerWithoutRedispatch` | 提供商承诺使用电子邮件应用程序, 然后所有者在存储前的回写失败。 回收保留了已承诺的尝试标记和负责操作员。 未知的查找保留了调节所有权和未来行动而不是再发布 。 |
| `ExhaustedRetryDecisionAndAuthorizedFollowUpSurviveImmediateSiloLoss` | 两次证明无效的尝试耗尽了预算,最终失效和单独授权的后续行动保留了其决定、独特身份以及直接 silo 损失之间的关联。 |

商业期望独立于恢复工作:库存
算术,一个供应商应用程序条目,稳定的逻辑键,不同的尝试
和明确的终端状态或对帐状态。
将生产编码器用于新对象的强制字节。它们不读
活动状态或保留其值的引用。

<a id="barriers-and-clocks"></a>

## 障碍和时钟

- `OutboxRetryScheduled` 描述 **中转**,而不是承诺。
  州-州-机关机关证人捕捉到
  只有在捕获后确认存储后`OnWriteCompleted`
- 送货过滤器在接收接收器进入前失败, 或者让接收器进入
  返回 `Accepted` 然后丢失了答案。它明确的第二次答
  隔离墙观察再交付,不管订单是否完成。
- `ScenarioJournalGate` 在进入储存或储存前持有一份有针对性的书面材料
  发件人确认后和继续调用之前。
  应用程序从独立日记字节中检查,而
  服务提供方的回答是持有的。
- 保留重写可推进现有固定 **持久工作时钟**。
  外部效应假设方案只推进配方 **键所有者时钟**
  并且明确援引其生产 `RunDueAsync` 处理器。
  今后仍保留工作交付;他们不要求交付时间表
  外部效应配方的覆盖范围。
- 时钟跟随实时加控制抵消。
  保留偶然过时毫秒的不相关。 没有睡眠或静默间隔
  设置完成。 圆墙时钟等待诊断的间隔与阶段,
  实体、任务状态、活动 silo、时钟和最后观察到的屏障。

<a id="failure-model-limits"></a>

## 失败示范模式限额

优雅启动解除功能、即时 silo 损失、日记账提供者失败
共同的波动性
供应商保留了在激活和 silo 损失之间复制的序列字节。
它不是一个持久性装置:这些试验并不确定OS-处理杀伤,
功率损失、 Fsync 或文件系统重新排序担保。 A 失败
笔记员写作练习
致命的中伤处理器

这些是实时应用情景, 不是兼容性重播测试 。
历史升级的兼容性仍然是现有
历史重现套房
