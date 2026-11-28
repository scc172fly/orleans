<a id="microsoft-orleans-durable-http"></a>

# Microsoft Orleans 持久化 HTTP

> [!WARNING]
> `Microsoft.Orleans.Durable.Http` 是处于 Alpha 阶段的预发行包，其 API 和线路协议可能在不同版本之间发生变化。

`Microsoft.Orleans.Durable.Http` 将 Orleans 持久化任务的幂等性扩展到 HTTP。ASP.NET Core Minimal API 的路由组能够将持久化 Grain 调用作为长时间运行的 HTTP 操作启动，以请求的 `Idempotency-Key` 为键，并提供操作的状态、结果、事件流、取消、信号及 Webhook。
`IDurableHttpClient` 将 HTTP 请求和操作作为持久化任务的步骤执行，并将每个步骤的标识作为 `Idempotency-Key` 发送，因此重试或重放请求的任务会访问首次尝试启动的操作。

<a id="getting-started"></a>

## 快速入门

Alpha 包是预发行包，因此使用 `--prerelease` 安装：

```shell
dotnet add package Microsoft.Orleans.Durable.Http --prerelease
```

<a id="serving-durable-operations"></a>

## 提供持久化操作服务

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

`WithDurableOperations` 使组内处理程序返回持久化 Grain 调用的每个端点启动执行该调用的操作。处理程序与其他 Minimal API 处理程序一样绑定参数，并在每次请求（包括重试）时运行；它创建调用，由端点启动调用。组内其他端点不受影响。身份验证和授权由应用负责：组上的策略和端点筛选器覆盖启动端点以及提供操作服务的路由。

请求的 `Idempotency-Key` 为操作命名，因此每次请求重试均访问首次尝试启动的操作。启动请求必须提供该键；除非 `DurableHttpOptions.RequireIdempotencyKey` 为 `false`，否则缺少该键的请求会收到 `400 Bad Request`。
操作标识符包含 1 至 255 个可打印 ASCII 字符，不含 `/`，且不为 `.` 或 `..`。

操作键由组的作用域、请求中的组路由参数值、调用方作用域以及操作标识符组成，每项为独立的段，例如 `orders/42/@ada/{id}`。作用域是稳定的名称，因此即使重命名组的路由也能保留原有操作；作用域和路由参数值均相同的路由组与 Nexus 服务会访问同一组操作。`DurableHttpOptions.GetCallerScope` 返回调用方的授权作用域，默认为已通过身份验证的用户的 `NameIdentifier` 或 `sub` 声明，因此调用方只能访问自身的操作：其他调用方即使知道某个操作标识符，也无法通过该标识符访问此操作。如果应用按帐户授权访问，则应返回帐户标识，就像 Stripe 按帐户划分幂等键作用域一样。匿名调用方共享一个作用域，缺少作用域的已认证调用方会被拒绝。

通过操作键寻址的操作宿主 Grain 在目标 Grain 上将调用作为根任务启动，以操作键为其名称，并以该调用的结果完成。因此观察、取消操作或向操作发送信号的请求仅需提供操作标识符即可访问该操作。处理程序也可以返回现有任务的引用，例如 `grain.GetDurableTask<Receipt>(id)`，由此启动订阅该任务的操作：操作报告任务的结果，发给操作的信号也会到达任务。取消该操作会结束订阅，而由其他方拥有的任务仍继续运行：

```csharp
orders.MapPost("/fulfillment", (string orderId, IGrainFactory grains) =>
    grains.GetGrain<IOrderGrain>(orderId).GetDurableTask<Shipment>("fulfillment"));
```

启动请求的响应如下：

| 响应 | 适用情况 |
| --- | --- |
| `200 OK` 及结果，或 `204 No Content` | 操作成功，包括在 `Prefer: wait=N` 请求的等待时限内完成的情况。 |
| `202 Accepted` 及状态监视器 | 操作仍在运行，包括操作完成前到达的重试；与 Azure 可重复请求和 Stripe v2 幂等机制一样恢复已有工作。 |
| `424 Failed Dependency` 及状态监视器 | 操作失败或被取消。如果执行调用的 Grain 在未存储调用时便拒绝它（例如该根任务标识符已在该 Grain 中用于另一个方法的任务），调用从未运行，错误码为 `NotStarted`；可使用新键重新启动。如果执行调用的 Grain 未确认调用（因为投递在未获确认时停止，或者在 Grain 的准入窗口结束后才到达），调用可能已运行，错误码为 `OutcomeUnknown`；重新启动前应检查调用的实际效果。 |
| `400 Bad Request` | `Idempotency-Key` 无效或缺失。 |
| `422 Unprocessable Content` | 另一请求已使用该键启动不同工作，即调用另一个方法、另一个 Grain，或订阅另一个任务。参数不同的重试仍在同一 Grain 上调用同一方法，因此仍访问同一操作，符合持久化任务运行时识别操作的方式。 |
| `503 Service Unavailable` 及 `Retry-After` | 请求未到达操作，操作保持不变，因此客户端使用相同的键重新发送请求。 |

标识操作的响应包含 `Operation-Location` 响应头，即其状态监视器的 URL：

```http
HTTP/1.1 202 Accepted
Operation-Location: https://example.com/orders/42/operations/8e03978e-40d5-43e8-bc93-6894a57f9324
Retry-After: 1

{"id":"8e03978e-40d5-43e8-bc93-6894a57f9324","status":"Running"}
```

状态监视器采用 Azure 长时间运行操作的结构：`status` 为 `Running`、`Succeeded`、`Failed` 或 `Canceled`；成功的操作携带 `result`，失败或被取消的操作携带 `error`，其中包含 `code` 和 `message`。`DurableHttpOptions.CreateError` 向客户端描述异常；默认仅报告 `Failed` 或 `Canceled`，因此异常详情保留在服务器端，除非应用显式映射自己为客户端定义的异常。组提供以下操作服务：

- `GET {group}/operations/{id}` 返回状态监视器。使用 `Prefer: wait=N` 时，响应最多等待 `N` 秒（不超过 `DurableHttpOptions.MaxWait`）以等待操作完成。正在运行的操作的响应携带 `Retry-After`。
- `GET {group}/operations/{id}/events` 以服务器发送的 `status` 事件流传输状态监视器，事件标识符为其状态：先发送当前状态，若操作正在运行，再发送终态，随后结束流。若重连请求的 `Last-Event-ID` 为终态，则返回 `204 No Content`，使 `EventSource` 停止重连。
- `POST {group}/operations/{id}:cancel` 持久化地请求取消操作，可携带 `{"reason": "..."}` 请求体，任务从 `DurableTask.Current.TaskCancellationReason` 读取原因；返回 `202 Accepted`。已处于终态的操作返回 `200 OK` 且保持不变。任务体决定任务的结果，由状态监视器报告。
- `POST {group}/operations/{id}/signals/{name}` 传递由 `MapDurableSignal` 映射的信号，使用 JSON 请求体作为信号值；任务持久化保存信号后返回 `204 No Content`，包括携带相同值的重复信号。没有请求体时返回 `400 Bad Request`。若值不同，或操作已完成且未收到该信号，则返回 `409 Conflict`。

执行工作任务的 Grain 保存任务后，操作宿主在日志中记录该任务。取消操作或向操作发送信号的请求从日志读取任务，而不依赖由哪个激活实例处理请求。返回 `202 Accepted` 的启动请求发生在记录完成之后；在记录之前到达的请求收到 `503 Service Unavailable` 及 `Retry-After`。

宿主在 `DurableTaskOptions.ResultRetentionPeriod` 指定的时限内保留已完成的操作，因此可按其键去重，并继续提供状态监视器。

提供操作服务的端点无需反射即可绑定请求，因此支持经过裁剪的应用和 NativeAOT 应用。信号值及操作结果使用 Minimal API 的 `JsonOptions` 序列化，在此类应用中，其解析器（例如源代码生成的 `JsonSerializerContext`）必须能够处理相应类型。

<a id="serving-durable-operations-through-nexus"></a>

## 通过 Nexus 提供持久化操作服务

Temporal 的 Nexus 协议通过服务名和操作名启动及取消操作。Nexus 服务将持久化 Grain 调用作为同一种操作启动：

```csharp
var payments = app.MapGroup("/nexus").MapNexusService("payments.v1", scope: "payments");
payments.MapNexusOperation("charge", (Charge input, IGrainFactory grains) =>
    grains.GetGrain<IPaymentGrain>(input.AccountId).ChargeAsync(input));

app.MapGroup("/nexus").WithDurableOperations("payments").MapDurableSignal(PaymentGrain.Capture);
```

- `POST /nexus/payments.v1/charge` 将调用作为请求的 `Nexus-Request-Id` 命名的操作启动。成功的操作返回 `200 OK`、结果及 `Nexus-Operation-State: succeeded`；运行中的操作返回 `201 Created` 及 `OperationInfo`，其中的 `token` 用于寻址该操作；失败或被取消的操作返回 `424 Failed Dependency` 及 `OperationError` 失败信息。
- `POST /nexus/payments.v1/charge/cancel` 持久化地请求取消由 `Nexus-Operation-Token` 请求头或 `token` 查询参数指定的操作，并返回 `202 Accepted`。
- 无效、冲突及未知请求分别收到 Nexus 处理程序错误：`400 BAD_REQUEST`、`409 CONFLICT` 和 `404 NOT_FOUND`。未到达操作的请求收到 `503 UNAVAILABLE`，调用方应重试。

`MapNexusOperation` 使用反射绑定处理程序的参数，与 Minimal API 绑定未被请求委托生成器拦截的 `MapPost` 处理程序的方式一致。

令牌就是操作标识符；可注册 `INexusOperationTokenFormat` 以签名、加密或采用其他方式格式化令牌。具有相同服务作用域且没有路由参数的组可以通过 Nexus 请求标识符寻址服务的操作，因此能够流式传输状态并向其投递信号。

<a id="callbacks"></a>

## 回调

启动操作的请求可以注册回调 URL，在操作进入终态后接收其结果：使用 `Operation-Callback` 请求头时，交付携带状态监视器及 Standard Webhooks 请求头；使用 Nexus 的 `callback` 查询参数时，交付 Nexus 完成通知。每次交付携带原请求中以 `Operation-Callback-` 或 `Nexus-Callback-` 开头的请求头（去掉前缀），例如接收方凭据。指定连接目标的请求头（如 `Host`、`Transfer-Encoding` 和 `Proxy-*`）以及交付过程自行设置的请求头（如 `webhook-*` 和 `Nexus-Operation-*`）不被接受，相关请求返回 `400 Bad Request`。

```csharp
builder.Services.AddDurableHttp(options =>
    options.AllowCallback = (context, url) => url.Scheme == "https" && IsTrustedReceiver(context.User, url));

siloBuilder.Services.AddDurableHttpCallbacks();
siloBuilder.Services.AddSingleton<IDurableWebhookSigner, MyWebhookSigner>();
```

`DurableHttpOptions.AllowCallback` 决定应用接受哪些 URL；默认不接受任何 URL，包含未获接受 URL 的请求收到 `400 Bad Request`。交付建立连接时，仅连接到 `DurableCallbackOptions.AllowAddress` 接受的地址（默认仅接受公有地址），因此即使应用接受 URL 后主机名才解析到内部地址，也不会访问该内部地址。交付不跟随重定向。回调客户端的主处理程序为执行此检查的 `SocketsHttpHandler`；若本包无法约束客户端的主处理程序，则客户端构建失败。

每个回调都是 Silo 中某个中继 Grain 上的持久化任务，它订阅操作并交付结果，直至接收方返回 `2xx` 或 `410 Gone`，或者 `DurableCallbackOptions.RetryTimeout` 到期。每次重试等待加倍退避间隔与接收方 `Retry-After` 指定间隔中的较大值，最多为 `DurableCallbackOptions.MaxRetryInterval`，再增加最多四分之一的抖动时长。重试启动请求会到达相同中继，该中继保留首次注册的请求头。交付采用至少一次语义：Webhook 的 `webhook-id` 在各次尝试中相同，持有应用密钥的 `IDurableWebhookSigner` 添加 `webhook-signature`。中继记录失败的尝试及最终放弃的交付。`AddDurableHttpCallbacks` 返回交付回调的 `HttpClient` 的构建器，用于配置其处理程序。

宿主在 Silo 存储回调请求头值（可能包含凭据）之前，使用 ASP.NET Core Data Protection 保护它们，因此宿主和 Silo 必须共享 Data Protection 密钥环及应用名称。

<a id="calling-http-apis-from-durable-tasks"></a>

## 从持久化任务调用 HTTP API

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

`AddDurableHttpClient` 注册 `IDurableHttpClient`，Grain 将其作为键控服务注入；该方法返回用于发送请求的 `HttpClient` 的构建器。客户端每个返回持久化任务的方法均将其响应记录为调用任务的步骤，因此重放读取这些响应，无需再次发送请求。每个步骤中使用非安全方法的请求携带 `Idempotency-Key`，该键由服务、Grain、步骤标识符及任务运行世代标识 `DurableTaskContext.Incarnation` 派生，因此故障或停用之后再次运行的步骤会访问相同的服务器端工作，而任务删除后使用同一任务标识符的后续运行会发送新的键。幂等键处理程序位于构建器添加的处理程序之前，因此这些处理程序的重试也携带该键。

- `SendAsync` 发送请求并返回已记录的 `DurableHttpResponse`。
- `RunOperationAsync<T>` 启动操作并以持久化方式等待结果；`StartOperationAsync` 和 `WaitForOperationAsync<T>` 分两步完成同一过程，可在两步之间执行其他工作。
- `GetOperationAsync`、`CancelOperationAsync` 和 `SignalOperationAsync` 通过操作状态监视器的 URL 读取、取消操作和向操作发送信号。
- `WatchOperationAsync` 在持久化执行之外通过事件流读取操作状态，直到操作进入终态。

`200 OK` 或 `204 No Content` 启动响应携带结果，不含状态监视器的其他 `2xx` 响应也如此。`424 Failed Dependency` 响应报告失败或被取消的操作。其他包含操作位置的响应表示操作仍在运行：可以是 `Operation-Location` 或 `Azure-AsyncOperation` 指定的状态监视器（例如本包提供的操作或 Azure 长时间运行操作，包括 Azure 的 `201 Created` 响应），也可以是 `202 Accepted` 响应中的 `Location`（在返回结果之前一直响应 `202 Accepted`）。默认情况下，客户端只跟随与启动请求同源的位置，具体由 `DurableHttpClientOptions.FollowOperationLocation` 决定，因此服务器无法将客户端的凭据导向另一个源。

`DurableHttpWaitMode.Poll` 在轮询之间使用持久化延迟：等待 `Retry-After` 声明的间隔，最短为 `DurableHttpOperationOptions.PollInterval`，最长为 `MaxPollInterval`；如果服务器已经按轮询的 `Prefer: wait` 偏好执行等待，则不再等待。收到 `408`、`429`、`502`、`503` 或 `504` 的轮询会再次轮询。`DurableHttpWaitMode.Events` 在一个步骤中读取状态监视器事件，并在服务器报告结果时立即获知结果。操作失败或被取消时抛出 `DurableHttpOperationException`，其中包含状态、错误码和消息。

已记录的响应省略 `Set-Cookie` 响应头，响应内容不超过 `DurableHttpClientOptions.MaxResponseContentLength`（默认为 1 MiB）；超过该大小的响应会导致对应步骤失败。

`DurableHttpClientOptions.SerializerOptions` 用于序列化请求值及结果，未另行配置时使用 Minimal API JSON 默认选项。经过裁剪的应用或 NativeAOT 应用应设置相应选项，使其解析器能够处理请求及结果类型。

`AddDurableIdempotencyKey` 向任意 `HttpClient` 添加幂等键处理程序，供通过 `DurableTask.Run` 在自身步骤中发送请求的持久化任务使用。

[DurableOrderProcessing 演练项目](../../playground/DurableOrderProcessing) 将这些部分结合起来演示：从浏览器持久化每次结账的键，到支付网关按键对授权请求去重。
