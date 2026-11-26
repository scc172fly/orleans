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

        // 本地方法作为执行过程的一部分运行, 所以开始时会分离投出 InvalidOperationException。
        _ = SurveyAsync(customer).WithId("survey").Detached().Post();
    }
    // </orleans0228_violation>
#pragma warning restore ORLEANS0228

    // <orleans0228_fix_grain>
    public async DurableTask CompleteAndSurveyAsync(string customer)
    {
        await Pack().WithId("pack");

        // 调查 grain 存储并运行该电话,作为一项根任务,在完成后继续。
        _ = GrainFactory.GetGrain<ICustomerSurveys>(customer).SendSurveyAsync(customer).WithId("survey").Detached().Post();
    }
    // </orleans0228_fix_grain>

    // <orleans0228_fix_attached>
    public async DurableTask CompleteThenSurveyAsync(string customer)
    {
        await Pack().WithId("pack");

        // 一步步在行刑时还是个孩子,等待着它。
        await SurveyAsync(customer).WithId("survey");
    }
    // </orleans0228_fix_attached>

    private static async DurableTask<bool> Pack() => true;

    private static async DurableTask<bool> SurveyAsync(string customer) => customer.Length > 0;
}
