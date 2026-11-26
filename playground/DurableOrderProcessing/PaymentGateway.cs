
namespace DurableOrderProcessing;

// 付款授权书的主体 请求进入网关。
public sealed record PaymentAuthorizationRequest(Guid OrderId, decimal Amount, string CardLast4);

// 网关授权状况监测器,以 Azure 长期运行作业的形式。
public sealed record PaymentAuthorizationStatus(string Id, string Status, PaymentResult? Result);

// 网关分类账的许可 获得/门道/授权名单
public sealed record PaymentAuthorizationEntry(string Id, Guid OrderId, decimal Amount, string Status, bool Voided, int Requests);

// 模拟外部付款网关,订单工作流程通过一个 IDurableHttpClient通过HTTP呼叫。
//
// 网关像黑条一样,是荣誉-关键。它记录了每个钥匙及其请求的体形,并回答
// 具有已知密钥和与第一个回应平等机构的请求,有标注的“能力强”重放。
// 以422个不可处理的内容拒绝另一个正文,而没有密钥的请求则以400个错误请求拒绝。每个密钥
// 因此,最多授权一次付款,以客户重报请求的次数为准。
//
// 授权无一例外地完成:请求返回202接受,并设有行动所在地状况监测器;
// 哪些报告在授权决定前运行, 而 POST { 监测}: cancel 宣布无效 。
// 以2000年结束的卡片号码被拒绝。对于以0003结束的卡片号码,则由网关决定,所以
// 授权运行到订单的支付超时失效。 对于以0004结束的卡号, 网关会掉下
// 连接而不是发送第一个密钥响应,因为网关记录后网络故障
// 授权;客户客户的复原力处理器使用相同的密钥重新发送请求。
// 5千元,网关无法接通,回答503 服务无可用,所以订单的付款失败。
public sealed class PaymentGateway(TimeProvider timeProvider)
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private static readonly TimeSpan DecisionLatency = TimeSpan.FromMilliseconds(500);

    private readonly object _lock = new();
    private readonly Dictionary<string, Authorization> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Authorization> _byId = new(StringComparer.Ordinal);

    public PaymentAuthorizationEntry[] List()
    {
        lock (_lock)
        {
            return [.. _byId.Values.OrderBy(authorization => authorization.CreatedAt).Select(authorization => authorization.ToEntry(timeProvider))];
        }
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var gateway = endpoints.MapGroup("/gateway");
        gateway.MapGet("/authorizations", () => List());

        gateway.MapPost("/authorizations", (HttpContext context, PaymentAuthorizationRequest request) =>
        {
            if (context.Request.Headers[IdempotencyKeyHeader] is not [{ Length: > 0 } key])
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "An Idempotency-Key header is required.");
            }

            if (request.CardLast4 == "0005")
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The gateway is unavailable.");
            }

            Authorization authorization;
            int attempt;
            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out authorization!))
                {
                    authorization = new Authorization(Guid.NewGuid().ToString("N"), request, timeProvider.GetUtcNow());
                    _byKey[key] = authorization;
                    _byId[authorization.Id] = authorization;
                }
                else if (authorization.Request != request)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "The Idempotency-Key is already used.",
                        detail: "Another request used this key. A key is reused only to retry the same request.");
                }

                attempt = ++authorization.Requests;
            }

            if (attempt > 1)
            {
                context.Response.Headers["Idempotent-Replayed"] = "true";
            }

            if (attempt == 1 && request.CardLast4 == "0004")
            {
                context.Abort();
                return Results.Empty;
            }

            context.Response.Headers["Operation-Location"] = $"/gateway/authorizations/{authorization.Id}";
            return Status(context, authorization, StatusCodes.Status202Accepted);
        });

        gateway.MapGet("/authorizations/{id}", (HttpContext context, string id) =>
            Find(id) is { } authorization ? Status(context, authorization, StatusCodes.Status200OK) : Results.NotFound());

        gateway.MapPost("/authorizations/{id}:cancel", (HttpContext context, string id) =>
        {
            if (Find(id) is not { } authorization)
            {
                return Results.NotFound();
            }

            lock (_lock)
            {
                authorization.VoidedAt ??= timeProvider.GetUtcNow();
            }

            return Status(context, authorization, StatusCodes.Status200OK);
        });
    }

    private Authorization? Find(string id)
    {
        lock (_lock)
        {
            return _byId.GetValueOrDefault(id);
        }
    }

    private IResult Status(HttpContext context, Authorization authorization, int statusCode)
    {
        PaymentAuthorizationStatus status;
        lock (_lock)
        {
            status = authorization.GetStatus(timeProvider.GetUtcNow());
        }

        if (status.Status == "Running")
        {
            context.Response.Headers.RetryAfter = "1";
        }

        return Results.Json(status, statusCode: statusCode);
    }

    // 帐簿上的授权 网关锁着可变成员
    private sealed class Authorization(string id, PaymentAuthorizationRequest request, DateTimeOffset createdAt)
    {
        public string Id => id;

        public PaymentAuthorizationRequest Request => request;

        public DateTimeOffset CreatedAt => createdAt;

        public int Requests { get; set; }

        public DateTimeOffset? VoidedAt { get; set; }

        // 网关在延迟后决定,但以千三时结束的卡片号码除外。
        private DateTimeOffset? DecidesAt => request.CardLast4 == "0003" ? null : createdAt + DecisionLatency;

        // 在决定取消授权之前即为无效;在发放授权付款之后即为无效。
        public PaymentAuthorizationStatus GetStatus(DateTimeOffset now)
        {
            if (DecidesAt is not { } decidesAt || decidesAt > (VoidedAt ?? now))
            {
                return new(id, VoidedAt is null ? "Running" : "Canceled", Result: null);
            }

            return new(id, "Succeeded", request.CardLast4 == "0002"
                ? new PaymentResult(false, "The card was declined.")
                : new PaymentResult(true, Reason: null));
        }

        public PaymentAuthorizationEntry ToEntry(TimeProvider timeProvider)
        {
            var status = GetStatus(timeProvider.GetUtcNow());
            var state = status.Result is { Succeeded: false } ? "Declined" : status.Status;
            return new(id, request.OrderId, request.Amount, state, Voided: VoidedAt is not null && status.Status == "Succeeded", Requests);
        }
    }
}
