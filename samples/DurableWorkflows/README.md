<a id="durable-orleans-workflows"></a>

# 持久 Orleans 工作流程

此样本以 Orleans 持久任务: grain 返回返回的方法 `DurableTask<T>`, which the
`Microsoft.Orleans.Durable` 在每个 grain日记中记录运行时间,并在停止运行或 silo 故障后恢复。
说明:

- 具有稳定根码和儿童任务识别卡的耐用 grain 电话;
- 含有一个名为 `DurableTask.WhenAll`的分支为 `branches/0`、 `branches/1`等名称的扇形/fan-in;
- 得到 `DurableSignal<T>` 的人力批准,期限从 `TryWaitAsync`开始;
- 长期以持续的理由和内线补偿取消对被保护儿童的合作(`.Shielded()`);
- 命令,用 `WhenAllCompleted` 解决同时步骤,并准确补偿成功步骤;
- 操作编号所标定的无能效应;
- (a)[和电子邮件效果协议](ExternalEffects.md) ,提供方钥匙、耐用再试验政策、调节和授权的后续行动;
- 恢复另一个服务复制的在编核准工作流程;
- 按根标识值对保留成果的观察。

圣子 AppHost 开始两个服务复制, Redis 用于 Orleans 以及长期的Azurite Blob 储存库
服务披露健康检查和没有固定港口的HTTP终点。

<a id="run"></a>

## 运行运行

```shell
aspire run --project DurableWorkflows.AppHost
```

打开 Aspire 仪表板中显示的服务端点。 例如请求, 其中 `$service` 是该端点:

```shell
curl -X POST "$service/workflows/basic/basic-1?input=hello"
curl -X POST "$service/workflows/fan-out/fan-1" -H "Content-Type: application/json" -d '["one","two","three"]'
curl -i -X POST "$service/workflows/approval/approval-1?subject=production"
curl "$service/workflows/approval/approval-1/status"
curl -X PUT "$service/workflows/approval/approval-1" -H "Content-Type: application/json" -d '{"subject":"production","approved":true,"reason":"reviewed"}'
curl "$service/workflows/approval/approval-1/status"
curl -i -X POST "$service/workflows/cancellation/hold-1"
curl "$service/workflows/cancellation/hold-1/status"
curl -X DELETE "$service/workflows/cancellation/hold-1?reason=operator-request"
curl "$service/workflows/cancellation/hold-1/status"
curl -X POST "$service/workflows/orders/order-1?failShipping=true"
```

<a id="workflows"></a>

## 流动流量

`WorkflowGrain` 托管每个工作流程。服务时间表根据从工作流程编号中得出的根号ID,
诸如 `approval-approval-1`,工作流程等待每个孩子 `WithId`儿童有身份证,例如
`approval-approval-1/decision`.工作流程ID每运行一个工作流点指定一个工作流程:该身份下反复提出的要求
现有工作流程并返回其状态或结果,而不论请求中的论点如何,例如另一种输入,
运行时间通过根 ID 和它使用的方法确定一个已计划的任务
而不是根据它的论点。

- **基本和扩展。** 工作流程每一步调用 `ActivityGrain` 一次。
  产生项目顺序,包括记录该顺序的工作的编号,例如 `fanout-fan-1/branches/2`。
- **批准。** 工作流程等待 `approval-decided` 信号直到其启动三天后,以其
  耐久时钟。 `PUT` 当工作流程不运行时启动工作流程, 并用
  `TrySignalDurableTaskAsync`.任务记录第一项决定:重复成功,以及另一项或一项不同的决定
  最后期限返回 `409 Conflict`。 A `PUT` 到达运行中的工作流程,不管它命名什么主题,所以
  结果报告工作流程启动的主题。
- **注销。** 工作流程储备库存并持有一天。 `DELETE` 当工作流量不时启动它
  运行时记录第一个请求及其理由
  工作,所以重放时要观察它们。工作流程用 `WhenAllCompleted`解决保留的结果,
  并释放被保护儿童成功的保留物 `.Shielded()`。
  与 `canceled: true` 以及原因。
- **秩序的圣人。** 工作流程储备库存和收费支付与 `WhenAllCompleted`,然后是船舶。
  如果航运失败,它退款并发放所继而使用的 `WhenAll`步骤。
  活动 grains 通过操作编号记录每个效果并返回重复操作的记录效果,所以要重放
  或重复的沙发对每个效果适用一次。

批准和取消命令返回 `202 Accepted` ,使用 `Location` 头列出 GET 状态资源。
状态响应包括根任务编号和 `pending`、 `succeeded`、 `canceled`或 `failed`的一个。
回复包括工作流程结果;取消和失败的回复包括安全摘要。
资源在命令之后而不是重复命令。

```json
{
  "taskId": "approval-approval-1",
  "status": "pending",
  "result": null,
  "error": null
}
```

不经安排时间调查保留的任务,然后返回 `404 Not Found` ,以完成从未开始的工作流程或
保留结果已过期的。失败的工作流程仍然是 `200 OK` 状态回复, 与 `status: "failed"`状态响应 。

对于手工失败过错, 启动一个批准工作流程, 停止在 Aspire 仪表板中进行 `service` 复制活动, 然后
测试套件终止了自有 silo ,而工作流程等待并观察其完成时间
其它 silo 通过原始任务控件。

<a id="guarantees-and-operational-boundaries"></a>

## 保障和作业边界

- 日记承诺每项持久任务的状况、 Orleans 通信收件箱和发件箱的修改以及 grain的耐久性
  每个复制品都使用相同的《日刊》和《持久工作》储存。
- 运行时间从一开始就重播未完成的工作流程,完成的儿童返回记录的结果。
  儿童之间代码在每次重放时计算相同的值;它读取时间
  `DurableTask.Current.UtcNow`.
- 与本样本中每个工作流程一样,客户计划的一项任务被保留用于
  `DurableTaskOptions.ResultRetentionPeriod` 完成后(24小时在此),在规定期限内,
  ID 返回所记录的结果;之后,它会返回 `404`,而日期表上的根 I 运行
  返回工作流程。任务中 grain 一旦使用此树的处决树, 则将引用符号删除 grain 接受
  结果。
- 与任务保留无关,本样本中的应用效果通过操作编号是无能的。
  外部 Orleans 新闻和通讯需要他们自己的能力。
- 监测耐用任务诊断和传递死亡信件,并将死亡信件和疲劳的重写作为
  操作可视失败。

<a id="package-availability"></a>

## 成套软件包的可用性

`Microsoft.Orleans.Durable`、 `Microsoft.Orleans.Durable.Abstractions`和 `Microsoft.Orleans.Durable.AzureStorage`
`samples/Build-Samples.ps1` 将当前源包装到本地种子并构建
此样本比照该精确版本。 通过此脚本构建样本, 直到软件包发布 。

<a id="test"></a>

## 测试测试

```shell
dotnet run --project DurableWorkflows.Tests/DurableWorkflows.Tests.csproj
```

测试项目是一个 Microsoft 测试平台应用软件,所以 `dotnet run` 运行其测试。测试在进行中
含有多变日记存储的Orleans 群集,因此不需要外部服务。
每项工作流程、信号拉链和最后期限、取消回收和补偿、SAR赔偿和
应急、交叉silo 回收、高专技术信托基金状况资源以及保留成果到期。
