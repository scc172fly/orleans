namespace Documentation.Diagnostics.DurableTasks;

// 外部服务为 Orleans以外的系统的客户提供服务。

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

// 支持 grain 接口,其耐久方法返回 DurableTask,例如命令 grain。
public interface IOrderWorkflows
{
    DurableTask<OrderResult> PlaceOrderAsync(Guid orderId);
}

// 其耐久方法的Grains ,其他示例也要求使用,所以独立启动将调用作为调用 grain的一个根任务。
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
