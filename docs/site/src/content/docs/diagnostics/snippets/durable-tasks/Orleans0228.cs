namespace Documentation.Diagnostics.DurableTasks;

public interface ICompletionSurveyGrain : IGrainWithStringKey
{
    DurableTask CompleteAsync(string customer);

    DurableTask CompleteAndSurveyAsync(string customer);

    DurableTask CompleteThenSurveyAsync(string customer);
}

public sealed class CompletionSurveyGrain : DurableGrain, ICompletionSurveyGrain
{
#pragma warning disable ORLEANS0228
    // <orleans0228_violation>
    public async DurableTask CompleteAsync(string customer)
    {
        await Pack().WithId("pack");

        // 本地方法作为当前执行实例的一部分运行，因此以分离模式启动会抛出 InvalidOperationException。
        _ = SurveyAsync(customer).WithId("survey").Detached().Post();
    }
    // </orleans0228_violation>
#pragma warning restore ORLEANS0228

    // <orleans0228_fix_grain>
    public async DurableTask CompleteAndSurveyAsync(string customer)
    {
        await Pack().WithId("pack");

        // 调查 Grain 将调用作为根任务存储并运行；该任务在当前执行实例完成后仍继续运行。
        _ = GrainFactory.GetGrain<ICustomerSurveys>(customer).SendSurveyAsync(customer).WithId("survey").Detached().Post();
    }
    // </orleans0228_fix_grain>

    // <orleans0228_fix_attached>
    public async DurableTask CompleteThenSurveyAsync(string customer)
    {
        await Pack().WithId("pack");

        // 该步骤作为此执行实例的子任务运行，当前执行实例等待它完成。
        await SurveyAsync(customer).WithId("survey");
    }
    // </orleans0228_fix_attached>

    private static async DurableTask<bool> Pack() => true;

    private static async DurableTask<bool> SurveyAsync(string customer) => customer.Length > 0;
}
