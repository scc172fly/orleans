
namespace DurableOrderProcessing;

// 发送给网关的支付授权请求体。
public sealed record PaymentAuthorizationRequest(Guid OrderId, decimal Amount, string CardLast4);

// 网关授权的状态监视器，采用 Azure 长时间运行操作的形式。
public sealed record PaymentAuthorizationStatus(string Id, string Status, PaymentResult? Result);

// 网关账本中的授权记录，由 GET /gateway/authorizations 列出。
public sealed record PaymentAuthorizationEntry(string Id, Guid OrderId, decimal Amount, string Status, bool Voided, int Requests);

// 模拟的外部支付网关，订单工作流通过 IDurableHttpClient 使用 HTTP 调用该网关。
//
// 网关与 Stripe 一样遵循 Idempotency-Key。它将每个键及其请求体一起记录，并在收到
// 已知键且请求体相同的请求时返回首次响应，标记为 Idempotent-Replayed。已知键与
// 不同请求体的组合将被拒绝，返回 422 Unprocessable Content；没有键的请求返回 400 Bad Request。因此，每个键
// 最多只会授权一次支付，无论客户端重试多少次。
//
// 授权异步完成：请求返回 202 Accepted，并通过 Operation-Location 提供状态监视器地址；
// 该监视器在授权产生结果前报告 Running，POST {monitor}:cancel 则撤销授权。
// 尾号为 0002 的银行卡被拒绝。尾号为 0003 的银行卡不会由网关给出授权结果，因此
// 授权持续运行，直到订单支付超时将其撤销。尾号为 0004 的银行卡会使网关
// 在首次响应该键时直接断开连接而不发送响应，模拟网关记录
// 授权后发生的网络故障；客户端的弹性处理器使用相同键重试请求。对于尾号为
// 0005 的银行卡，网关不可用并返回 503 Service Unavailable，因此订单支付失败。
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

    // 账本中的授权记录。网关的锁保护其可变成员。
    private sealed class Authorization(string id, PaymentAuthorizationRequest request, DateTimeOffset createdAt)
    {
        public string Id => id;

        public PaymentAuthorizationRequest Request => request;

        public DateTimeOffset CreatedAt => createdAt;

        public int Requests { get; set; }

        public DateTimeOffset? VoidedAt { get; set; }

        // 除尾号为 0003 的银行卡外，网关在延迟一段时间后给出授权结果。
        private DateTimeOffset? DecidesAt => request.CardLast4 == "0003" ? null : createdAt + DecisionLatency;

        // 在授权产生结果前撤销，会取消授权；之后撤销，则释放已授权的支付金额。
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
