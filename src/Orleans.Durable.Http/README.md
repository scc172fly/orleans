<a id="microsoft-orleans-durable-http"></a>

# Microsoft Orleans 耐久 HTTP

> [!WARNING]
> `Microsoft.Orleans.Durable.Http` 是一个释放前的阿尔法包件。其API和电线
> 协议可以在释放之间改变。

`Microsoft.Orleans.Durable.Http` 承担 Orleans 持久任务的最佳能力
横跨 HTTP 的路径组。 ASP.NET 核心最低APP开始耐用 grain 调用电话
以请求 `Idempotency-Key`为关键,长期运行的HTTP操作,并提供服务
状态、结果、事件流、取消、信号和网络图。
`IDurableHttpClient` 处理HTTP的请求和业务,作为持久任务的步骤;
将每步的标识作为 `Idempotency-Key`发送,所以要进行重试或重试的任务
重播请求,使其第一次尝试开始的行动达到。

<a id="getting-started"></a>

## 入门

Alpha 软件包是预释放的, 所以用 `--prerelease`安装此软件包 :

```shell
dotnet add package Microsoft.Orleans.Durable.Http --prerelease
```

<a id="serving-durable-operations"></a>

## 长期维持业务

```csharp
builder.Services.AddDurableHttp();

var app = builder.Build();

var orders = app.MapGroup("/orders/{orderId}")
    .WithDurableOperations("orders")
    .RequireAuthorization("orders");

orders.MapPost("/place", (string orderId, PlaceOrder order, IGrainFactory grains) =>
    grains.GetGrain<IOrderGrain>(orderId).PlaceAsync(order));
orders.MapDurableSignal(OrderGrain.Approval);
```

`WithDurableOperations` 将处理器返回
调用耐久 grain 启动一个操作,以进行操作。处理者将其绑定
任何最小 API 处理器, 并运行每项请求, 包括重试;
组创建调用, 端点开始。 其他端点是
验证和授权是应用程序的:政策和
该组上端端端点过滤器覆盖其启动和运行路线
业务。

请求的 `Idempotency-Key` 中指定了操作,因此每次重试请求
到达它第一次尝试开始的操作。启动需要键,并且
a 没有收到 `400 Bad Request` 的请求,除非
`DurableHttpOptions.RequireIdempotencyKey` 是 `false`。操作标识符
1至255个除 `/`以外的可打印 ASCII 字符,且不是 `.` 或 `..`。

操作的密钥是该组的范围、请求中该组路线的价值
参数、调用对象的范围、识别符号,每个部分,例如
以 `orders/42/@ada/{id}`命名。范围是一个稳定的名称,因此重新命名该组的路线
保持其业务; 组合和Nexus服务,一个范围,同一路线值
地址相同操作。 `DurableHttpOptions.GetCallerScope` 返回调用器的
默认情况下,认证用户的 `NameIdentifier` 或 `sub`的授权范围
因此,一个打电话者只能达到自己的业务:另一个打电话者所持的识别特征
申请授权书的申请人,可申请批准。
账户访问后返回账户, 因为 Strake 使用账户密钥。 匿名
呼叫者有同一个范围,而一个没有范围的认证来电者则被拒绝。

操作主机 grain,即该操作的密钥地址,以一个
以操作的钥匙命名的其 grain根任务,并完成结果;
要求观察、取消或信号运行,通过识别符号到达
。单一个处理器返回
现有任务,例如 `grain.GetDurableTask<Receipt>(id)`的引用开始于
操作中报告任务结果,
其信号到达任务。取消行动结束其订阅,
其他各方自己承担的任务继续:

```csharp
orders.MapPost("/fulfillment", (string orderId, IGrainFactory grains) =>
    grains.GetGrain<IOrderGrain>(orderId).GetDurableTask<Shipment>("fulfillment"));
```

开始回复 :

| 回应答复答复回应回应回应回应回应回应回应回应回应回应回应回应回应回应回应回应 | 何时 |
| --- | --- |
| `200 OK` 与结果,或 `204 No Content` | 操作成功, 包括在 `Prefer: wait=N` 请求的等待范围内 。 |
|  具有状态监视器的 `202 Accepted` | 操作正在运行中,包括完成前到的重试,因为 Azure 重复请求和Srake的 v2 应急恢复工作。 |
|  具有状态监视器的 `424 Failed Dependency` | 操作失败或被取消。 当调用 grain 拒绝时, 没有保存它, 例如调用根已经命名了其他方法任务的调用, 调用从未运行, 错误代码是 `NotStarted`; 新密钥又开始。 当调用 grain 不确认它, 因为其发送已退出, 并且未识别或者到达 grain的入门窗口后, 调用可能已经运行, 错误代码是 `OutcomeUnknown`; 在重新启动调用前检查调用的效果 。 |
| `400 Bad Request` | `Idempotency-Key` 无效,或丢失。 |
| `422 Unprocessable Content` | 另一项请求使用键启动其他工作:呼叫另一种方法或另一种 grain,或者订阅另一任务。如果参数不同,则重试同一方法 grain,这样就可以实现操作,因为持久任务运行时间可以识别操作。 |
| `503 Service Unavailable` 与 `Retry-After` | 请求没有到达操作, 没有改变, 所以客户再次用同样的钥匙寄过来。 |

用于识别操作的响应,该响应包含 `Operation-Location` 页眉,即 URL
其地位监测员:

```http
HTTP/1.1 202 Accepted
Operation-Location: https://example.com/orders/42/operations/8e03978e-40d5-43e8-bc93-6894a57f9324
Retry-After: 1

{"id":"8e03978e-40d5-43e8-bc93-6894a57f9324","status":"Running"}
```

状态监视器的形状为 Azure 长期运行操作: `status` 是
`Running`, `Succeeded`, `Failed`,或 `Canceled`;成功的行动带有其
`result`,而失败或取消的操作带有 `error` 具有 a 的 `code` 和 a 以及
`message`. `DurableHttpOptions.CreateError` 描述客户的例外;
只报告 `Failed` 或 `Canceled`,所以服务器上存在例外细节
除非应用程序绘制了它为其客户定义的例外。
服务于下列业务:

- `GET {group}/operations/{id}` 返回状态监视器。与 `Prefer: wait=N`,
  最多等待 `N` 秒,最多等待 `DurableHttpOptions.MaxWait`秒
  运行中的操作的响应包含 `Retry-After`。
- `GET {group}/operations/{id}/events` 将状态监视器作为服务器已同意流出
  `status` 事件,按其状况确定:现状,然后是终点
  运行中的操作状态,在此之后流结束。
  `Last-Event-ID` 接收 `204 No Content`,以结束一个 `EventSource`
  重新连接。
- `POST {group}/operations/{id}:cancel` 长期请求取消,并
  任务所读取的任择 `{"reason": "..."}` 机构
  `DurableTask.Current.TaskCancellationReason`,返回 `202 Accepted`;终端
  返回 `200 OK` 。任务正文决定结果,而
  状况监测报告。
- `POST {group}/operations/{id}/signals/{name}` 发送信号
  `MapDurableSignal` 地图, JSON 体为其值的地图和返回
  一旦任务长期坚持,包括重复信号,即为`204 No Content`
  请求中不带正体返回 `400 Bad Request`。 A
  返回
  `409 Conflict`.

操作的主机将完成的工作记录在日记中一次
任务 grain 所在的任务中保留了该任务,请求取消或信号操作的请求读作
从那里开始任务, 不论激活为它们服务。 返回的起始
`202 Accepted` 记录和在它收到之前收到的请求,
`503 Service Unavailable` 与 `Retry-After`.

东道主保留已完成的操作,因此使钥匙变重复并服务于
`DurableTaskOptions.ResultRetentionPeriod`的状态监视器。

行动终点对请求具有约束力,不加考虑,因此它们为
缩刻和 NativeAOT 应用程序。信号值和操作结果与
最小 API `JsonOptions`,其确定者,例如源产生的
`JsonSerializerContext`,处理这些应用中的这类类型。

<a id="serving-durable-operations-through-nexus"></a>

## 通过Nexus为持久业务提供服务

时间时空 Nexus 协议开始并取消运行, 按服务和操作来取消操作
Nexus 服务开始以相同的操作进行耐用 grain 调用:

```csharp
var payments = app.MapGroup("/nexus").MapNexusService("payments.v1", scope: "payments");
payments.MapNexusOperation("charge", (Charge input, IGrainFactory grains) =>
    grains.GetGrain<IPaymentGrain>(input.AccountId).ChargeAsync(input));

app.MapGroup("/nexus").WithDurableOperations("payments").MapDurableSignal(PaymentGrain.Capture);
```

- `POST /nexus/payments.v1/charge` 调用开始作为
  请求 `Nexus-Request-Id` 名称。 它返回 `200 OK` 并产生结果和
  成功操作`Nexus-Operation-State: succeeded` , `201 Created`
  `OperationInfo` 其 `token` 运行时的地址,
  `424 Failed Dependency` 失败或取消的 `OperationError` 失败
  运行中。
- `POST /nexus/payments.v1/charge/cancel` 长期请求取消[
  其业务活动的 `Nexus-Operation-Token` 标题或 `token` 查询价值名称,
  返回 `202 Accepted`。
- 无效、 冲突、 未知的请求接收 Nexus 处理器错误 :
  `400 BAD_REQUEST`、 `409 CONFLICT`和 `404 NOT_FOUND`。
  正在接受 `503 UNAVAILABLE`,调用者将重试。

`MapNexusOperation` 将处理器的参数反射绑定, 因为最小API 绑定
a `MapPost` 处理器,请求代表发电机无法拦截。

标记是操作的标识符; 注册 `INexusOperationTokenFormat`
符号。
服务使用 Nexus 请求的线路路线参数
识别符号,所以它会流出他们的身份 并传递他们的信号。

<a id="callbacks"></a>

## 回回调

启动操作请求时登记回调 URL,该URL接收
运行结果一旦是终端时的结果: `Operation-Callback` 头,其
带有标准 Webhookes 头或 Nexus 的状态监视器, 或 Nexus 。
`callback` 查询值,其交付完成是Nexus完成的。
请求书页头以 `Operation-Callback-` 或 `Nexus-Callback-`开头,
上下前缀, 如接收者的证书。
连接,例如 `Host`、 `Transfer-Encoding`和 `Proxy-*`等
装货货物,如 `webhook-*` 和 `Nexus-Operation-*`等,接收
`400 Bad Request`.

```csharp
builder.Services.AddDurableHttp(options =>
    options.AllowCallback = (context, url) => url.Scheme == "https" && IsTrustedReceiver(context.User, url));

siloBuilder.Services.AddDurableHttpCallbacks();
siloBuilder.Services.AddSingleton<IDurableWebhookSigner, MyWebhookSigner>();
```

`DurableHttpOptions.AllowCallback` 确定应用程序接受的 URL;
默认它不接受任何条件, 而请求中会收到另一个 URL `400 Bad Request`。
交付仅连接到 `DurableCallbackOptions.AllowAddress`地址
默认公用公用信标,当它们连接时,它会接受。所以一个主机名可以解决
内部地址,包括申请被接受后,没有达到。
送货人不会遵循方向调整
应用检查的`SocketsHttpHandler` 客户端,其主处理器
软件包无法限制无法构建 。

每次回调都是中继的持久任务 grain 中在 silos,其中的用户
操作并交付结果,直到接收者回答 `2xx` 或
`410 Gone`或 `DurableCallbackOptions.RetryTimeout` 折叠。每次重试等待
最大为双倍后退和接收器 `Retry-After`的
`DurableCallbackOptions.MaxRetryInterval`,加最多四分之一以上。
到达同一中继站, 保持第一个注册的头头。
至少一次:每件尝试的网钩 `webhook-id` 等于每个尝试,
附加了`IDurableWebhookSigner`,该键持有应用程序的密钥
`webhook-signature`.中继日志失败尝试, 以及它提供的送货
上楼 `AddDurableHttpCallbacks` 返回返回 `HttpClient` 减去
发送回调,以配置其处理器。

东道主保护回调头的值, 可以是证书, 包括
ASP.NET 核心数据保护 silos 故你们将那些寄存的、实成宿的, silos a 共享份额a
数据保护关键环和应用程序名称。

<a id="calling-http-apis-from-durable-tasks"></a>

## 将HTTP APP 宣传广告从长期任务中调离

```csharp
siloBuilder.Services.AddDurableHttpClient("payments")
    .ConfigureHttpClient(client => client.BaseAddress = paymentsUri)
    .AddStandardResilienceHandler();

public sealed class OrderGrain([FromKeyedServices("payments")] IDurableHttpClient payments) : DurableGrain, IOrderGrain
{
    public static readonly DurableSignal<string> Approval = new("approval");

    public async DurableTask<Receipt> PlaceAsync(PlaceOrder order)
    {
        var payment = await payments.StartOperationAsync(() => new HttpRequestMessage(HttpMethod.Post, "authorizations")
        {
            Content = JsonContent.Create(new Authorization(order.Account, order.Total)),
        });

        try
        {
            var approver = await Approval.WaitAsync();
            await payments.SignalOperationAsync(payment.Location!, "capture", order.Total);
            return await payments.WaitForOperationAsync<Receipt>(payment, new() { Mode = DurableHttpWaitMode.Events });
        }
        catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
        {
            await payments.CancelOperationAsync(payment.Location!, DurableTask.Current.TaskCancellationReason).Shielded();
            throw;
        }
    }
}
```

`AddDurableHttpClient` 登记 `IDurableHttpClient`, grains 作为
键键服务,并返回它发送请求的 `HttpClient` 构建器
其每一方法都以恢复持久任务记录的方式,作为步骤答复
重播后再读一遍,不发请求。
步骤的不安全要求 `Idempotency-Key` 服务产生的 grain,
步骤的标识符,任务运行, `DurableTaskContext.Incarnation`,所以是一个步骤
发生故障或停止功能后再次运行的服务器端工作,
后运行同一任务标识符,删除后,发送新密钥。在
建筑者加添的处理器,他们的变形器携带钥匙。

- `SendAsync` 发送请求并返回所记录的 `DurableHttpResponse`。
- `RunOperationAsync<T>` 开始操作,并长期等待结果;
  `StartOperationAsync` 和 `WaitForOperationAsync<T>` 分两个步骤这样做,其他
  两者的工作。
- `GetOperationAsync`、 `CancelOperationAsync`和 `SignalOperationAsync` 改为:
  取消, 并用状态监视器的 URL 信号操作 。
- `WatchOperationAsync` 运行状态的流经于外部
  直至处决结束为止。

`200 OK` 或 `204 No Content` 的启动响应和另一个一样,都产生效果
`2xx` 没有状态监视器的反应。 A `424 Failed Dependency` 答复报告的答复
运行操作的地址
:a 国家 `Operation-Location` 或 `Azure-AsyncOperation` 状态监视器, 例如此
软件包的操作和 Azure 长期运行的操作服务,包括 Azure
`201 Created` 或反应,或 `Location` 上a `202 Accepted` 答复,回答
`202 Accepted` 直到它返回结果。客户端跟随启动位置
请求的原发国,由 `DurableHttpClientOptions.FollowOperationLocation` 决定,因此
服务器无法将客户的证书 直接送到另一个服务器

民意调查`DurableHttpWaitMode.Poll` 民 ,两次投票之间拖延时间长:
广告`Retry-After` ,至少 `DurableHttpOperationOptions.PollInterval` 和
最大 `MaxPollInterval`,在应用民意测验的服务器后没有
`Prefer: wait`. 一次再次接受 `408`、 `429`、 `502`、 `503`或 `504` 民意测验的民调。
`DurableHttpWaitMode.Events` 将状态监控器的事件读取为一步骤内,
服务器一报告结果,即学习结果。失败或取消操作
以其状态、代码和消息扔出 `DurableHttpOperationException` 。

录音答复中略略 `Set-Cookie` 页眉,其内容最多
`DurableHttpClientOptions.MaxResponseContentLength`,默认一个中比字节;较大的
其对策失败了。

`DurableHttpClientOptions.SerializerOptions` 将请求的数值和结果序列成序,
与最少的 API JSON 除非配置了,否则默认值。 NativeAOT
应用程序设置了解决方案,由解决者处理其请求和结果类型。

`AddDurableIdempotencyKey` 将密钥处理器添加到任何 `HttpClient`,以完成持久任务
以 `DurableTask.Run`发送自步骤提出的请求。

运行这些 [DurableOrderProcessing 操场](../../playground/DurableOrderProcessing)
从一个浏览器, 维持每个退开的密钥到
他们的钥匙把授权复制了