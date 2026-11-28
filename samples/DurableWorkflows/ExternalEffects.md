<a id="payment-and-email-effects"></a>

# 支付及邮件效果

`ExternalEffectGrain` 在自身日志中负责各效果的生命周期。提供程序负责支付授权或
邮件接受结果。对账记录应用程序的证据及决定。`RecipeEffectProviderGrain`
是具有独立日志的演示服务，因此这些实践方案可通过示例的 Aspire 主机在本地运行。
生产网关使用支付或邮件提供程序的 API 实现相同的派发及查询契约。

<a id="run-a-payment-and-its-outgoing-receipt"></a>

## 执行支付并发送其收据

按照 [README.md](README.md) 的说明启动示例。在 PowerShell 中，将 `$service` 设置为 Aspire
仪表板中的服务 URL。随订单保留标识及原始请求；重复提交时复用这些值。

```powershell
$service = 'https://localhost:YOUR_SERVICE_PORT'
$order = 'payment-' + [guid]::NewGuid().ToString('N')
$account = 'provider-' + $order
$identity = @{
    executionId = 'checkout'; incarnation = [guid]::NewGuid().ToString()
    operationId = 'authorize'; occurrence = 0
}
$request = @{
    kind = 0; providerAccount = $account; destination = 'customer-account'
    content = $order; amount = 12.50; currency = 'USD'
    onSuccess = @{
        kind = 1; providerAccount = $account; destination = 'customer@example.invalid'
        content = "Receipt for $order"
    }
}
function Send-EffectCommand($path, $body) {
    Invoke-RestMethod "$service/$path" -Method Post -ContentType 'application/json' `
        -Body (ConvertTo-Json -InputObject $body -Depth 10)
}
$accepted = Send-EffectCommand "external-effects/$order/" @{ identity = $identity; request = $request }
# Durable Jobs dispatches committed pending effects. /run also advances work which is already due.
Invoke-RestMethod "$service/external-effects/$order/run" -Method Post
$payment = Send-EffectCommand "external-effects/$order/inspect" $identity
$payment
$receiptIdentity = $identity.Clone()
$receiptIdentity.operationId = 'authorize/on-success'
$receipt = Send-EffectCommand "external-effects/$order/inspect" $receiptIdentity
$receipt
```

`kind` 为 `0` 表示支付授权，为 `1` 表示幂等邮件，为 `2` 表示中断后需要对账的邮件，
为 `3` 表示支付退款。授权成功时，将
`businessState: "authorized;ready-to-fulfill"` 与后续邮件
意图一同记录。其 `nextEffectKey` 标识该意图。持久化发件箱在同一次写入中暂存发往
`EffectOrderGrain` 的结果消息；接收方收件箱将订单投影与消息完成操作一同提交。
所有者的效果记录仍是生命周期的权威来源。监控 Messaging 死信，并根据该记录对
耗尽重试窗口的投递进行对账。

`SubmitAsync` 确认意图已提交。`ExternalEffectWorkflowGrain.RunAsync` 演示持久化任务
组合：从 `DurableTask.Current` 创建标识，在具名步骤中提交意图，并使用具名
读取及持久化延迟等待已记录的终态结果。

```csharp
var workflow = client.GetGrain<IExternalEffectWorkflowGrain>(orderId);
var handle = await workflow.RunAsync(paymentRequest).WithId("checkout").ScheduleAsync();
var result = await handle.WaitAsync(cancellationToken);
```

工作流保留的结果是所有者终态结果的快照。停止客户端等待或取消
轮询工作流会停止该观察。当业务操作应停止派发时，通过其所有者的 `CancelAsync` 或 `/cancel`
操作取消效果。

<a id="exercise-a-lost-response-and-a-decline"></a>

## 演练响应丢失与拒绝

Development 环境公开演示提供程序的控制接口。提交效果前，先配置新的提供程序账户。
提供程序行为值为 `Apply`、`Reject`、`ProveNoEffect`、`LoseResponseAfterApply`、
`LookupUnavailable` 和 `UnavailableBeforeApply`。

```powershell
$order = 'lost-' + [guid]::NewGuid().ToString('N')
$account = 'provider-' + $order
$identity.incarnation = [guid]::NewGuid().ToString()
$request.providerAccount = $account
$request.content = $order
Invoke-RestMethod "$service/external-effect-providers/$account/behavior/LoseResponseAfterApply" -Method Post
$accepted = Send-EffectCommand "external-effects/$order/" @{ identity = $identity; request = $request }
Invoke-RestMethod "$service/external-effects/$order/run" -Method Post
Start-Sleep -Seconds 6
Invoke-RestMethod "$service/external-effects/$order/run" -Method Post
Send-EffectCommand "external-effects/$order/inspect" $identity

$order = 'decline-' + [guid]::NewGuid().ToString('N')
$account = 'provider-' + $order
$identity.incarnation = [guid]::NewGuid().ToString()
$request.providerAccount = $account
$request.content = $order
Invoke-RestMethod "$service/external-effect-providers/$account/behavior/Reject" -Method Post
Send-EffectCommand "external-effects/$order/" @{ identity = $identity; request = $request }
Invoke-RestMethod "$service/external-effects/$order/run" -Method Post
Send-EffectCommand "external-effects/$order/inspect" $identity
```

响应丢失使本次尝试的结果不确定。安全重试使用原始提供程序键，提供程序返回
其记录的授权结果。拒绝以 `Rejected` 完成，带有 `businessState: "business-rejected"`，并创建
拒绝结果消息。仅在授权已生效时创建成功邮件意图。

<a id="reconcile-unkeyed-email-and-authorize-a-deliberate-resend"></a>

## 对无幂等键邮件进行对账，并授权明确的重新发送

无幂等键邮件网关演示能够接受邮件并支持运维人员辅助调查的提供程序。
响应丢失时，原始请求、键、尝试证据、解决责任人、原因和下次检查时间
会保留在所有者日志中。调度的查询在失败或返回无定论证据时保留不确定性。

```powershell
$order = 'email-' + [guid]::NewGuid().ToString('N')
$account = 'provider-' + $order
$emailIdentity = @{
    executionId = 'notification'; incarnation = [guid]::NewGuid().ToString()
    operationId = 'receipt'; occurrence = 0
}
$emailRequest = @{
    kind = 2; providerAccount = $account; destination = 'customer@example.invalid'; content = 'Your receipt'
}
Invoke-RestMethod "$service/external-effect-providers/$account/behavior/LoseResponseAfterApply" -Method Post
Send-EffectCommand "external-effects/$order/" @{ identity = $emailIdentity; request = $emailRequest }
Invoke-RestMethod "$service/external-effects/$order/run" -Method Post
Start-Sleep -Seconds 6
Invoke-RestMethod "$service/external-effects/$order/run" -Method Post
$pending = Send-EffectCommand "external-effects/$order/inspect" $emailIdentity
$pending | Select-Object status, providerKey, attempts, evidence, resolutionOwner, nextAction, nextActionAt, revision

# After obtaining the provider receipt, the authorized operator records the decision.
# ProviderOutcome: Applied=0, Rejected=1, ProvenNoEffect=2, Unknown=3.
Send-EffectCommand "external-effects/$order/resolve" @{
    identity = $emailIdentity; expectedRevision = $pending.revision
    outcome = 0; evidence = 'Support case 42: provider receipt verified'
}

# A customer-requested second copy is a new operation with its own key and explicit authorization.
$resend = $emailIdentity.Clone()
$resend.operationId = 'customer-requested-copy'
Send-EffectCommand "external-effects/$order/follow-up" @{
    original = $emailIdentity; followUp = $resend
    authorization = 'Support case 43: customer authorized a second copy'
}
```

在活动尝试结束后授权后续操作。原效果指向新效果；新效果
保留原始键及授权。未解决的原效果继续保留并进行对账，
但禁止后续派发。对账决定使用显示的修订版本，因此更新的决定优先于
迟到的提供程序响应。过期修订版本返回 `409 Conflict`；重新决定之前先检查当前记录。
部署服务时，使用应用程序身份验证及授权保护取消、问题解决、策略修改、后续操作授权
和提供程序诊断。

<a id="protocol-and-policy"></a>

## 协议及策略

标识由所有者 Grain ID、持久化执行 ID、根运行实例标识、稳定的操作名称和
发生序号组成。`EffectIdentity.Current("authorize", occurrence)` 捕获任务持久化的运行标识。循环迭代
使用不同的发生序号值。对无歧义的 JSON 数组执行 SHA-256 计算得到提供程序键。尝试 GUID 与
持久化子任务 ID 保持独立。重复标识返回已记录的请求或结果；冲突的请求
内容会显式失败。新授权的业务操作获得新标识。`/on-success` 后缀
保留给由原始请求派生的传出意图。

每个效果对其策略创建快照：版本、执行模式、最大消耗尝试次数、退避、尝试超时、
总体截止时间、幂等安全截止时间及对账间隔。服务从
`ExternalEffects` 配置节绑定默认值。新配置适用于新提交；恢复使用已存储的
策略、已消耗尝试次数和下一操作时间。对于尚未尝试派发的效果，`ChangePolicyAsync` 接受经验证且显式授权的
策略变更。不受支持的已存储策略保持阻止状态，附带
运维人员操作要求及调度检查。软件升级可恢复对使用此类策略且已尝试派发的效果的支持。
`AddExternalEffectRecipes()` 注册所有者的键控 `TimeProvider`（`external-effects`），并使失败的
基础设施唤醒仍符合重试条件。持久化效果策略独立限制向提供程序的派发。

支持安全重试的支付和幂等邮件网关保证同一个键在已存储安全
截止时间之前可重复使用。根据提供程序保证的保留期保守设置 `ProviderKeyLifetime`，考虑时钟
不确定性及最大请求处理延迟。所有者仅在一次尝试的超时能够严格落在
总体截止时间及安全截止时间之前时才接纳该尝试。截止时间已过或重试预算耗尽会将
不确定效果转为查询或运维人员对账。一系列经证明确实未生效的尝试可以以
确认失败完成。较晚尝试的未生效故障会保留此前尝试产生的不确定性。

对于中断后需要对账的邮件，所有者在派发前持久化尝试标记。标记写入后、
派发前发生崩溃同样属于结果不确定的情况。提供程序拒绝是业务结果；文档明确说明的未生效
故障仅适用于其所描述的尝试。超时、普通异常、响应丢失及取消
均保留可能已生效的判断。提供程序适配器仅在具有权威的
逻辑键结果时返回 `Applied` 或 `Rejected`，仅在其契约能够确立该证据时返回 `ProvenNoEffect`。

每次包含可执行待办工作的写入都会先确认 Durable Jobs 唤醒。意图写入失败不会触发提供程序
派发。即使完成记录写入失败，已提交标记及远程成功结果也可通过恢复及
原始键保留。完成操作、相关业务状态、下一效果意图及结果发件箱消息共用
所有者的一次日志写入。查询对账证据之前同样会先建立下一次唤醒。

尝试取消是协作式且局部的。持久化效果取消记录请求、停止后续
派发，并保留任何可能已生效的结果，以供查询或运维人员处理。已确认成功的支付
可能需要退款，退款被建模为单独授权的补偿效果。本实践方案保留所有记录。
生产保留策略应保留未解决的记录，并至少在
应用程序重放窗口及提供程序幂等窗口内保留已完成的标识。

例如，`AuthorizeFollowUpAsync(authorizationIdentity, refundIdentity, approvalReference,
paymentRequest with { Kind = EffectKind.PaymentRefund, OnSuccess = null })` 记录退款的授权
及其与原支付的关系。原始授权及其收据意图保持不变。
