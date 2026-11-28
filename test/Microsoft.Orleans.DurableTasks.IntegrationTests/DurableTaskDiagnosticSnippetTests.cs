using Documentation.Diagnostics.DurableTasks;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Durable.Tasks;
using Orleans.Runtime;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.IntegrationTests;

/// <summary>在集群上运行 ORLEANS0219 页面中的分离启动修复示例。</summary>
[Collection(DurableTaskRecoveryCollection.Name)]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("DurableTasks")]
public sealed class DurableTaskDiagnosticSnippetTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly EmailSender _email = new();
    private readonly DurableTaskRecoveryFixture _fixture;

    public DurableTaskDiagnosticSnippetTests()
    {
        _fixture = new(DurableTaskRecoveryFixture.OrleansBinary, silo => silo.Services.AddSingleton<IEmailSender>(_email));
    }

    public ValueTask InitializeAsync() => _fixture.InitializeAsync();
    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Orleans0219DetachedSurveyContinuesAfterTheDispatchCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string customer = "customer@example.com";
        var dispatcher = _fixture.Client.GetGrain<IDispatchGrain>(Guid.NewGuid().ToString("N"));

        var dispatch = await dispatcher.DispatchAndSurveyAsync(customer).WithId("dispatch").ScheduleAsync(cancellationToken);
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully,
            (await dispatch.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).Status);

        // 调查任务是客户调查 Grain 上的根任务，其根 ID 由派发任务根据自己的 Grain、
        // ID 及操作名称派生；派发任务完成后，调查任务继续运行。
        var message = await _email.Started.Task.WaitAsync(Timeout, cancellationToken);
        Assert.Equal((customer, "How was your order?"), (message.Recipient, message.Subject));
        var surveyId = TaskId.CreateRoot($"{TaskId.CreateRoot(dispatcher.GetGrainId().ToString())}/{TaskId.CreateRoot("dispatch").Child("survey")}");
        var survey = _fixture.Client.GetGrain<ICustomerSurveys>(customer).GetDurableTask<object>(surveyId);
        Assert.False(survey.GetResponseAsync(cancellationToken).IsCompleted);

        _email.Complete.TrySetResult();
        Assert.Equal(DurableTaskStatus.CompletedSuccessfully,
            (await survey.GetResponseAsync(cancellationToken).WaitAsync(Timeout, cancellationToken)).Status);
        Assert.False(message.CancellationToken.IsCancellationRequested);
    }

    private sealed class EmailSender : IEmailSender
    {
        public TaskCompletionSource<(string Recipient, string Subject, CancellationToken CancellationToken)> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendAsync(string recipient, string subject, CancellationToken cancellationToken)
        {
            Started.TrySetResult((recipient, subject, cancellationToken));
            await Complete.Task.WaitAsync(cancellationToken);
        }
    }
}
