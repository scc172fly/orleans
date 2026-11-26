<a id="payment-and-email-effects"></a>

# 支付和电子邮件效果

`ExternalEffectGrain` 在其日记本中拥有每种效应的生命周期。
核对记录申请的证据和决定。 `RecipeEffectProviderGrain`
这是一种独立出版的演示服务, 所以这些配方是本地与样本的“ 精选主机” 一起运行的。
生产网关使用付款或电子邮件提供者的 API执行同样的发送和查询合同。

<a id="run-a-payment-and-its-outgoing-receipt"></a>

## 运行付款及其寄出收据

开始 [README.md](README.md)中描述的样本。在 PowerShell中,将 `$service` 设为从寄件者处到服务 URL
仪表板:保持身份和原始请求与订单保持一致;重复提交文件,再使用这些数值。

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

`kind` 是 `0` 付款授权, `1` 用于无能力电子邮件, `2` 需要电子邮件对账
中断和 `3` 还款。
`businessState: "authorized;ready-to-fulfill"` 和下一封电子邮件
其 `nextEffectKey` 确定了这一意图。
在同一封信中`EffectOrderGrain` ;收件箱承诺其顺序投影,并完成邮件的填写。
物主的效应记录仍然是生命周期当局。
它将用那记录来用尽它重新试镜的窗口。

`SubmitAsync` 确认承诺意图。 `ExternalEffectWorkflowGrain.RunAsync` 证明是持久任务
成分:它从 `DurableTask.Current`中创建身份,在指定的步骤中提交意图和名称的使用
并持续拖延直到记录到最终结果。

```csharp
var workflow = client.GetGrain<IExternalEffectWorkflowGrain>(orderId);
var handle = await workflow.RunAsync(paymentRequest).WithId("checkout").ScheduleAsync();
var result = await handle.WaitAsync(cancellationToken);
```

工作流程保留的结果是简单描述业主的终端结果。
取消其通过所有人 `CancelAsync` 或 `/cancel`的效果
当业务运作应停止时,即开始作业。

<a id="exercise-a-lost-response-and-a-decline"></a>

## 演习失败反应和下降

开发环境暴露了示范供应商控制。
提供方行为值为 `Apply`, `Reject`, `ProveNoEffect`, `LoseResponseAfterApply`,
`LookupUnavailable`、和 `UnavailableBeforeApply`。

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

丢失的响应使尝试无法确定。安全重试使用原始供应商键和提供者返回
注销额的计算截止 `Rejected` 与 `businessState: "business-rejected"` 创建
拒绝结果信息。 成功电子邮件意向仅为应用授权创建 。

<a id="reconcile-unkeyed-email-and-authorize-a-deliberate-resend"></a>

## 调和不加密的电子邮件并授权故意重发

提供网络服务, 接受电子邮件,
失去答复后,原始请求、钥匙、尝试证据、分辨率所有人、理由和下次检查时间都落空
排定的查询在无法或没有确定的证据归还时保留不确定性。

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

积极尝试解决后授权采取后续行动。 最初指向新效果; 新效果
保留原始钥匙和授权。未解决的原始钥匙仍保留,并继续进行调节
将未来发送禁用后, 未来发送会停止。 和解决定使用显示的修改, 这样新的决定就会在
延迟提供方回复。 一份老旧的修订返回 `409 Conflict`; 在再次决定前检查当前记录 。
保护取消、解决、政策改变、后续授权和供应商应用诊断
部署的单位的认证和授权。

<a id="protocol-and-policy"></a>

## 议定书和政策

身份由自有 grain ID、耐用执行ID、根运行化、稳定操作名称和
发生。 `EffectIdentity.Current("authorize", occurrence)` 捕捉任务的持续运行身份。
使用不同发生次数值。 在一个毫不含糊的 JSON 数组上的 SHA-256 生成提供方密钥。
儿童任务的持续持久身份识别特征仍然分开。重复身份身份返回其记录的请求/结果;要求冲突
内容明显失败。新授权的商务操作获得新的身份。 `/on-success` 后缀是
保留给原请求书的原始输出意图。

每种效果都与政策相似: 版本,执行模式,最大消耗尝试,后退,尝试超时
整个最后期限、应急安全期限和对账间隔。
`ExternalEffects` 配置一节。新的配置管理新提交材料;回收使用存储的
`ChangePolicyAsync` 接受经审定的、明确授权的
用于效果的政策改变没有尝试发送。
操作员动作和预定检查。软件升级将恢复对尝试效果的支持,并采用这样的政策。
`AddExternalEffectRecipes()` 登记所有者键 `TimeProvider` (`external-effects`),并保持失败
持续有效政策独立限制供应商发送。

重试安全付款和无权限电子邮件网关承诺通过存储的安全性重复使用一个密钥
。从供应商的保证保留中保守地设定 `ProviderKeyLifetime` ,允许按时
所有人承认,只有在超时符合要求的情况下才试图这样做。
期限已过或已用尽的预算转换
一系列经证明的无效果尝试可以作为一个
稍后的尝试“无效果失败”使先前的尝试有任何不确定性。

对于对断对账电子邮件,所有者在发送前坚持一个尝试标记。
提供商拒绝是一种商业结果;一种有记录的无效果
失败仅适用于其描述的尝试。超时、通用例外、丢失回复和取消
仅对权威用户使用供应商的调适器返回 `Applied` 或 `Rejected`
逻辑关键结果,以及 `ProvenNoEffect` 只有在合同证明证据时才有。

每一个待处理的写作都首先承认“持久工作提醒 ” 。 失败的意向写作将离开服务商
完成失败的文字,通过恢复和
完成、相关商业状态、下一个效果意图和结果发件人信息共享
下一次提醒在查询对账证据之前就已经确定。

取消企图是合作性的,是当地当地的合作。 持久效果取消请求记录了请求,停止未来
并保存任何可能适用于查找或操作员的任何结果。
要求退款后可要求退款,以单独核准的补偿效力为模式。
保持生产活动保留生产量的作业保存了未解决的记录,至少保持了
应用程序重放窗口和供应商的“即时”窗口。

例如,`AuthorizeFollowUpAsync(authorizationIdentity、 refundIdentity、 approvalReference,
paymentRequest ,{种类= EffectKind.PaymentRefund, OnSuccess = null} `记录退款授权'
原始授权和接收意向保持不变。
