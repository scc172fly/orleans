---
title: "将耐久任务与其他系统整合"
description: "电话支付提供者、数据库和HTTP服务,来自长期任务,有应急钥匙,作为HTTP业务的持久任务,接收网络挂图作为信号,并选择如何重新尝试瞬时故障。"
ms.date: 10/17/2026
ms.topic: how-to
---

<a id="integrate-durable-tasks-with-other-systems"></a>

# 将耐久任务与其他系统整合

这些配方将耐久的任务与 Orleans以外的系统:付款提供者、
长期运行的HTTP服务、启动耐久 HTTP客户
以及回溯的网络图示。
每一个外部呼叫都按一个步骤进行,记录其结果,发出一无所不能的信号
关键字源自任务, 因为一个其结果没有执行的一步会再次运行。 The
外部客户是接口,因此这些实例没有提供商的SDK就汇编。
[持久任务中具有的](durable-tasks-idempotency.md) 解释担保
这些食谱所依赖的。

<a id="choose-where-to-retry"></a>

## 选择要在哪里重新尝试

故障是 *短暂的*,例如超时、利率限值或断电,如果
以后尝试可以克服,或 *永久*,例如拒绝卡、验证错误,
或冲突,而每一尝试都重复出现。在水平上重试转轨失败
与它们持续的时间相符,并处理永久失败的结果:

| 失败失败 | 在那里重新找过 | 如何 |
| --- | --- | --- |
| 无法发送或处理一个 grain 呼叫或一个持久任务启动 | 通信通信 | 自动,直到 `OutboxRetryTimeout` (默认为6小时),然后 `DurableTaskDeliveryException`。工作流程不重试。 |
| 短短的外出过失,秒长 | 步内 | 循环或恢复管道,如Polly's或 `IDurableHttpClient`的抗御处理者,围绕呼叫,在每次尝试中都使用同样的应急钥匙。 |
| 外部断电,时至小时长 | 跨步骤 | 与 `DurableTask.Delay` 后撤相连接的连续步骤,这些步骤在解除功能之后仍能生存下来,并受逻辑时间中最后期限的约束。 |
| 拒绝,如拒绝付款、冲突或审定错误 | 无处无 | 把它从阶梯上还回来,作为价值,或赶上它的例外,并补偿或报告它。 |

仅将明确拒绝作为永久:一个任务标识已经启动的开始
其它方法的命名失败 `DurableTaskConflictException`的目标,以及
拒绝有自订代码的请求, 无法以 `DurableTaskRejectedException`失败 。

<a id="call-a-payment-api-with-an-idempotency-key"></a>

## 调用一个有应急密钥的付款 API

**任务:** 通过供应商向客户收费,而供应商将一客户的请求与
`Idempotency-Key`,例如条形。

提供商的客户报告下降,作为例外情况暂时无法提供:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_payment_provider" language="csharp":::

将工作流程处理的结果转换成一个值,所以该步骤
记录结果显示重播结果发生后发生的结果:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_charge_outcome" language="csharp":::

键将 grain的身份、任务的身份和操作的名称组合在一起,
每一次重播和尝试,都是同样的。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_idempotency_key" language="csharp":::

工作流程收费,在 `DurableTask.Run` 级中重新列出短暂缺勤
步内二倍:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_charge_in_step" language="csharp":::

- 步骤接收 `DurableTask.Current.CancellationToken`,工作流程的任务就是接收该步骤
  取消该步骤的尝试,并取消取消该步骤的尝试。
  和客户端的尝试间隔时间。
- 每次尝试都发送同样的钥匙, 所以提供商就收费一次。当 silo 失败失败
  下个运行期发送
  同一键再次, 供应商返回原始电荷 。
- 拒绝的卡片返回一个值。步骤完成,工作流程继续
  在每个重播中都出现下降。
- 抗御管道,如Polly `ResiliencePipeline`,取代内部环圈
  代表以同样的方式:在步骤内用同一键和
  步骤取消标记 。

空虚 :

- 计算跨步重试循环之外的关键键,从横跨稳定的值中计算
  重放。 从 `Guid.NewGuid()` 中键取自每个重播放的正文变化;见
  [ORLEANS0201](../diagnostics/orleans0201.md).
- 保持步进后退短列。 该步骤在它重试时执行执行, 并且
  取消取消该步骤的尝试, 下一步从第一个开始, 下一运行重新开始
  重试。

**校验 :** 排 grain 列表列表 `charge` 年为 `Completed` 和
供应商的仪表板显示钥匙的一个电荷,任何重复请求都标有:
即兴重播

<a id="retry-across-steps-with-durable-backoff"></a>

## 带有耐用后卸回的跨步重试

**任务:** 继续试图通过供应商中断收费,中断持续数小时,没有
持有 grain 活动状态。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_charge_with_backoff" language="csharp":::

每次尝试都是一个步骤: 循环圈名称 `charge`、 `charge$2`等等,
延误 `charge-backoff`、 `charge-backoff$2`。报告无法完成的一步
与该结果相伴随,工作流程等待供应商的 `Retry-After` 和耐用
延迟, 而 grain 可以在等待时停止运行。 每个记录的一个回放返回
并恢复未完成的延迟。
时间,所以每次重播都计算出相同的一个。

空虚 :

- 以最后期限或尝试计算的方式绕过重试,并决定工作流程
  发生事故时,如补偿或报告失灵。
- 每次尝试和延误都会给任务树上增加记录,直到树被删除。 A
  工作流量中,如果在短间隔内重试天数时使用较长的间隔或分离
  后续任务。

**核查:** 供应商无法提供服务,但 grain 将尝试列为 `Completed`
与未取得结果和直到到期的最新 `charge-backoff` 为 `Delayed`
时间。

<a id="write-to-a-database"></a>

## 写入数据库

**任务:** 将装运记录在关系数据库中,并一次性作出决定
数据库当前内容。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_database" language="csharp":::

- **读数按步骤运行。** 工作流程从行中决定 `find` 步
  记录,因此重放使用同一分支,即使表格自此变化。
- **写作由操作ID键入。** 上调,对 ID 设置了独有的限制,
  等 `INSERT ... ON CONFLICT (operation_id) DO NOTHING`,应用重复写
  。没有上层科技的商店将操作编号记录在与
  更改,并在应用修改前检查。
- **每笔交易一步** 一项交易如果作出若干变更,则在其中一宗交易中发生
  步骤,所以它的改变和操作身份 一起承诺,该步骤的记录
  告诉工作流程他们承诺了。

<a id="deliver-database-changes-to-grains"></a>

### 发送对 grains的数据库更改

更改数据库并必须启动其写作工作流程的服务
在同一交易中,一个列列到发件台。一个中继器读取表格并开始
以行命名的每行耐久任务 :

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_outbox_relay" language="csharp":::

列的 ID 使启动的负主机: 在一行标记为
重新开始行的任务, 从而达到现有任务。 接收 grain
任务状态是收件箱。大小 `ResultRetentionPeriod` 超过时间间隔之间最长的延迟
列的第一和最后一中继

**核查:** 数据库每运行ID一行,每个运行编号都有一个数据库,并转发同一个发箱行
两次离开 grain ,其中一项任务名为 `outbox-{id}`。

<a id="call-http-apis-from-durable-tasks"></a>

## 将长期任务中的HTTPAP APP调用

**任务:** 向运行长期操作的服务机构提交标签,等待
标签,当工作流程被取消时取消任务。

下 `Microsoft.Orleans.Durable.Http` 软件包包 `IDurableHttpClient` 运行的 HTTP
要求和操作作为调用任务的步骤。
silo,配置 `HttpClient` 以建筑者为 `AddDurableHttpClient`
返回,并输入客户端为密钥服务:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_http" language="csharp":::

- **能力关键来自职档身份。** 每一步的不安全要求 都包含
  `Idempotency-Key` 服务产生的 grain,该步骤的任务识别码,
  任务运行后, `DurableTaskContext.Incarnation`的跳进,因此一个步骤在
  失败或停止功能的终止达到其第一次尝试开始的工作,然后
  运行相同任务ID 发送新密钥。 密钥处理器先于创建者添加的操作器,
  例如抗御能力处理器, 所以他们的重心 带有相同的键。
- **答复记录在案。** 重放播放每一步的录音反应
  再次发出请求。
- **业务遵循长期业务惯例。** A `202 Accepted` 答复回复回应
  将操作定位为 `Operation-Location`、 `Azure-AsyncOperation`或
  `Location`. `DurableHttpWaitMode.Events` 读取状态监视器服务器已发事件
  中间段内一个步骤,和间隔后 `DurableHttpWaitMode.Poll` 的投票
  `Retry-After` 在任务调查间隔内广告,并有长期拖延
  两次民意调查之间, 所以等待很长的等待不能让 grain 活动保持活跃。 `WaitAsync` 约束
  逻辑时间里有最后期限等待。 失败或取消操作会丢弃
  `DurableHttpOperationException`.
- **取消到达远程操作。** `CancelOperationAsync` 发送
  `POST {status monitor}:cancel` ,任务取消的原因或最后期限,
  屏蔽,在工作流程取消后运行。 `SignalOperationAsync` 交付
  信号也是一样

以 `DurableTask.Run` 发送自发请求的阶步从
`HttpClient` 减去 `AddDurableIdempotencyKey` 配置。对于每种方法和选项,
见 [软件包 README](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable.Http/README.md)。

**核查:** 标签服务每步获得一份工作,但经常
重新审理步骤的要求, grain 列出开始和等待的阶梯,每个
`Completed` 及其记录的答复。

<a id="serve-durable-tasks-as-http-operations"></a>

## 长期任务作为HTTP业务

**任务:** 让 HTTP 客户端开始返回, 重试
安全地提出请求,跟踪进展情况,取消请求,并提交检查结果。

`WithDurableOperations` 设置一个路线组的每一端点,其处理器返回a
耐久 grain 调用启动一个操作,以进行:

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_http_operations" language="csharp":::

- ** `Idempotency-Key` 中指定操作。** 请求的每一次重写都达到
  开始运行, 第一次尝试开始, 没有按键的启动接收
  `400 Bad Request`. 操作的键将集团的范围、路线值、
  来电者的授权范围和识别特征 所以来电者只能达到
  自动操作,而调用以此密钥命名的根任务运行。
  用于启动其他工作的另一项请求、 另一种方法的调用或其它
  grain,接收 `422 Unprocessable Content`;重试其参数不同达到
  操作。
- **状况监测报告进展情况。** 开始回答 `200 OK` 与
  包括 `Prefer: wait=N` 请求的等待内,
  和 `202 Accepted` ,加上 `Operation-Location` 页眉,即
  `GET {group}/operations/{id}`现况监测报告 `Running`, `Succeeded`,
  `Failed`或 `Canceled`,结果或错误,并给予 `Prefer: wait=N`荣誉。
- **事件流出状态。** `GET {group}/operations/{id}/events` 流出状态
  显示服务器- 发送事件, 直到操作为终端, 所以浏览器
  `EventSource` 之后,他们不投票。
- **客户端取消并发送任务信号。** `POST {group}/operations/{id}:cancel` 请求
  具有理由的持久取消,以及 `POST {group}/operations/{id}/signals/{name}`
  发送一个信号,该信号将绘制 `MapDurableSignal` 地图。操作的主机记录任务
  以其日记本开展工作,因此取消和发出信号就达到这一任务
  不论激活者为他们服务
- **申请授权是申请的** 集团的政策和过滤器
  开始运行的路线

操作的主机保留了已完成的操作,因此使关键部分发生重复
`ResultRetentionPeriod`. The [软件包 README](https://github.com/dotnet/orleans/blob/main/src/Orleans.Durable.Http/README.md) 描述每一份答复,
Nexus 协议, 以及将操作结果传送到 Webhook URL 的回调 。

**核查:** 重复 `POST` ,与 `Idempotency-Key` 返回相同
`Operation-Location`,状况监测监测报告行动的结果及其
以终端状态结束的串行事件。

<a id="receive-webhooks-and-callbacks-as-signals"></a>

## 接收网络呼头和回调作为信号

**任务:** 等待付款提供人的网络钩确认收费。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_webhook" language="csharp":::

网络端点验证 Webhook 的签名, 并调用普通 grain 方法,
将确认作为信号发送 `TrySignalAsync`。用于执行的任务
HTTP 操作, `MapDurableSignal` 的交货与
`POST {group}/operations/{id}/signals/{name}`。任务记录
信号的第一个值, 所以:

- 工作流量等待前到达的网络 Hook , 且等待返回
  一次。
- 提供同一事件服务再次接收的提供者 `Duplicate`,其中的终点
  承认成功状态, 供应商停止重新尝试。
- 相同信号返回 `Conflict`的值不同,端点报告
  和木头,
- `TryWaitAsync` 将等待与一个可持久期限相约束。

用于报告一个任务的若干事件 Webhoooks 其中每个任务使用一个信号名, 并来自
具有 `DurableSignal<T>.For`,例如每份收费身份证一张。

**核查:** 第一发货返回 `Accepted`,重复发货返回
`Duplicate`,工作流程与确认结果完全一致。

<a id="handle-rejections-without-retrying"></a>

## 处理拒绝不重试的拒绝

**任务:** 在企业身份证明下启动后续调查,处理无法启动的启动
成功成功。

:::code source="../snippets/compiled-durable/Grains/DurableTaskIntegrationSnippets.cs" id="durable_tasks_rejections" language="csharp":::

- `DurableTaskConflictException` 报告说,身份证已经指定了另一种方法。
  在同一ID下重新试用同一身份的, 冲突也达到相同的冲突, 因此工作流程就能解决它, 因为
  示例,通过阅读现有任务或选择其它 ID 来查找。
- `DurableTaskRejectedException` 报告一个目标,即以其自己的目标拒绝请求
  代码,在 `Code`中。
- `DurableTaskDeliveryException` 报告一个开始, 即通信无法在
  `OutboxRetryTimeout`.工作流程将它当作一个断电, 超过它的
  截止日期。
- 申请结果,如拒绝卡或库存外线等,是
  和 grain 方法返回,所以这些步骤上的工作流程分支不重试。

**核查:** 在同一ID下以同一方法和不同方法第二个开始
参数达到第一个任务,该 ID 下的起始时会投出另一种方法
`DurableTaskConflictException`, 目标的死信记录
`durable-rpc/conflict` 代码;见 [死亡信](durable-tasks-operations.md#dead-letters)。
