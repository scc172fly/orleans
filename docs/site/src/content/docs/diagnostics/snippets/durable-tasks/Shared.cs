namespace Documentation.Diagnostics.DurableTasks;

// 示例共享的声明。外部服务用于模拟 Orleans 外部系统的客户端。

public enum OrderStatus
{
    Submitted,
    Reserved,
    Paid,
    Shipped,
    Cancelled,
}

[GenerateSerializer, Immutable]
public sealed record Charge(string ChargeId, decimal Amount);

[GenerateSerializer, Immutable]
public sealed record Decision(string ApproverId, bool Approved);

[GenerateSerializer, Immutable]
public sealed record OrderResult(OrderStatus Status, string? Reason);

public interface IPaymentGateway
{
    Task<Charge> ChargeAsync(string idempotencyKey, decimal amount, CancellationToken cancellationToken);
}

public interface IExchangeRates
{
    Task<decimal> GetRateAsync(string currency, CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, CancellationToken cancellationToken);
}

public interface IDocumentHasher
{
    Task<string> HashAsync(byte[] document, CancellationToken cancellationToken);
}

public interface IRoomInventory
{
    Task ReleaseAsync(string room, CancellationToken cancellationToken);
}

// 模拟某个 Grain 接口，其持久化方法返回 DurableTask，例如订单 Grain 的接口。
public interface IOrderWorkflows
{
    DurableTask<OrderResult> PlaceOrderAsync(Guid orderId);
}

// 其他示例调用这些 Grain 的持久化方法，因此分离启动会在目标 Grain 上将调用作为根任务运行。
public interface ICustomerSurveys : IGrainWithStringKey
{
    DurableTask SendSurveyAsync(string customer);
}

public sealed class CustomerSurveysGrain(IEmailSender email) : DurableGrain, ICustomerSurveys
{
    public async DurableTask SendSurveyAsync(string customer) =>
        await email.SendAsync(customer, "How was your order?", DurableTask.Current.CancellationToken);
}

public interface IStudentMail : IGrainWithStringKey
{
    DurableTask<bool> WelcomeAsync(string student, int seat);
}

public sealed class StudentMailGrain(IEmailSender email) : DurableGrain, IStudentMail
{
    public async DurableTask<bool> WelcomeAsync(string student, int seat)
    {
        await email.SendAsync(student, $"Welcome! Your seat is {seat}.", DurableTask.Current.CancellationToken);
        return true;
    }
}
