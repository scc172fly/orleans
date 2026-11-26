using Microsoft.AspNetCore.Mvc;
using Orleans.Durable.Tasks;

namespace DurableWorkflows;

public static class WorkflowEndpoints
{
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromDays(3);
    private static readonly TimeSpan HoldPeriod = TimeSpan.FromDays(1);
    private static readonly PollingOptions PollImmediately = new() { PollTimeout = TimeSpan.Zero };

    public static IEndpointRouteBuilder MapDurableWorkflowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Text(
            "Durable Orleans workflows sample. See README.md for basic, fan-out, approval, cancellation, saga, and recovery scenarios."));

        endpoints.MapPost("/workflows/basic/{workflowId}", RunBasicAsync);
        endpoints.MapPost("/workflows/fan-out/{workflowId}", RunFanOutAsync);
        endpoints.MapPost("/workflows/approval/{workflowId}", StartApprovalAsync);
        endpoints.MapPut("/workflows/approval/{workflowId}", SubmitApprovalAsync);
        endpoints.MapGet("/workflows/approval/{workflowId}/status", GetApprovalStatusAsync);
        endpoints.MapPost("/workflows/cancellation/{workflowId}", StartCancellationAsync);
        endpoints.MapDelete("/workflows/cancellation/{workflowId}", CancelAsync);
        endpoints.MapGet("/workflows/cancellation/{workflowId}/status", GetCancellationStatusAsync);
        endpoints.MapPost("/workflows/orders/{orderId}", RunOrderAsync);
        return endpoints;
    }

    // 客户等待其根据工作流程编号产生的根ID排在工作流编号下所安排的工作流量结果。
    // 重复请求时, 随任何输入返回工作流程的结果, 而 grain 保留了该结果 。
    private static async Task<IResult> RunBasicAsync(
        IClusterClient client,
        string workflowId,
        string input,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        var workflow = client.GetGrain<IWorkflowGrain>(workflowId);
        var scheduled = await workflow.RunBasicAsync(input).WithId(RootId("basic", workflowId)).ScheduleAsync(cancellationToken);
        return Results.Ok(await scheduled.WaitAsync(cancellationToken));
    }

    private static async Task<IResult> RunFanOutAsync(
        IClusterClient client,
        string workflowId,
        [FromBody] string[]? items,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        // JSON 无效体约束为空;空白列表没有要运行的分支。
        if (items is not { Length: > 0 })
        {
            return InvalidItems();
        }

        var workflow = client.GetGrain<IWorkflowGrain>(workflowId);
        var scheduled = await workflow.RunFanOutAsync(items).WithId(RootId("fanout", workflowId)).ScheduleAsync(cancellationToken);
        return Results.Ok(await scheduled.WaitAsync(cancellationToken));
    }

    private static async Task<IResult> StartApprovalAsync(
        IClusterClient client,
        string workflowId,
        string subject,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return InvalidSubject();
        }

        var scheduled = await ScheduleApprovalAsync(client, workflowId, subject, cancellationToken);
        return Accepted("approval", workflowId, scheduled.Id);
    }

    // 当批准尚未运行时启动批准, 然后将决定信号到其根任务。 任务记录
    // 首先决定,所以重复一遍,这是一无是处的, 和不同的决定冲突。
    private static async Task<IResult> SubmitApprovalAsync(
        IClusterClient client,
        string workflowId,
        ApprovalSubmission? submission,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        // JSON 无效机构以无效形式约束。
        if (submission is null || string.IsNullOrWhiteSpace(submission.Subject))
        {
            return InvalidSubject();
        }

        var scheduled = await ScheduleApprovalAsync(client, workflowId, submission.Subject, cancellationToken);

        var result = await client.GetGrain<IWorkflowGrain>(workflowId).TrySignalDurableTaskAsync(
            RootId("approval", workflowId),
            WorkflowSignals.ApprovalDecided,
            new ApprovalDecision(submission.Approved, submission.Reason),
            cancellationToken);
        return result switch
        {
            DurableSignalResult.Accepted or DurableSignalResult.Duplicate => Accepted("approval", workflowId, scheduled.Id),
            DurableSignalResult.TaskNotFound => Missing(),
            _ => Conflict(),
        };
    }

    private static Task<IResult> GetApprovalStatusAsync(
        IClusterClient client,
        string workflowId,
        CancellationToken cancellationToken) =>
        GetStatusAsync<ApprovalWorkflowResult>(client, "approval", workflowId, cancellationToken);

    private static async Task<IResult> StartCancellationAsync(
        IClusterClient client,
        string workflowId,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        var scheduled = await ScheduleCancellationAsync(client, workflowId, cancellationToken);
        return Accepted("cancellation", workflowId, scheduled.Id);
    }

    // 请求持久取消工作流程, 要求在运行尚未开始时从启动流程开始。 运行时间记录
    // 第一项请求及其任务的理由,工作流程决定其结果。
    private static async Task<IResult> CancelAsync(
        IClusterClient client,
        string workflowId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        var scheduled = await ScheduleCancellationAsync(client, workflowId, cancellationToken);
        await scheduled.CancelAsync(reason, cancellationToken);
        return Accepted("cancellation", workflowId, scheduled.Id);
    }

    private static Task<IResult> GetCancellationStatusAsync(
        IClusterClient client,
        string workflowId,
        CancellationToken cancellationToken) =>
        GetStatusAsync<CancellationWorkflowResult>(client, "cancellation", workflowId, cancellationToken);

    private static async Task<IResult> RunOrderAsync(
        IClusterClient client,
        string orderId,
        bool failShipping,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(orderId))
        {
            return InvalidId();
        }

        var workflow = client.GetGrain<IWorkflowGrain>(orderId);
        var scheduled = await workflow.RunOrderSagaAsync(new(orderId, failShipping))
            .WithId(RootId("order", orderId)).ScheduleAsync(cancellationToken);
        return Results.Ok(await scheduled.WaitAsync(cancellationToken));
    }

    // 将现有的根身份表排成日程,不论要求标出什么主题,都可达到工作流程。
    private static Task<ScheduledTask<ApprovalWorkflowResult>> ScheduleApprovalAsync(
        IClusterClient client,
        string workflowId,
        string subject,
        CancellationToken cancellationToken) =>
        client.GetGrain<IWorkflowGrain>(workflowId)
            .RunApprovalAsync(new(subject, ApprovalTimeout))
            .WithId(RootId("approval", workflowId)).ScheduleAsync(cancellationToken);

    private static Task<ScheduledTask<CancellationWorkflowResult>> ScheduleCancellationAsync(
        IClusterClient client,
        string workflowId,
        CancellationToken cancellationToken) =>
        client.GetGrain<IWorkflowGrain>(workflowId)
            .RunCancellationAsync(HoldPeriod)
            .WithId(RootId("cancellation", workflowId)).ScheduleAsync(cancellationToken);

    // 将留存的根任务不安排时间,而将保留的任务投平。 grain 保留客户计划完成的任务结果
    // 完成后DurableTaskOptions.ResultRetentionPeriod ; 丢失或过期的任务返回404。
    private static async Task<IResult> GetStatusAsync<TResult>(
        IClusterClient client,
        string kind,
        string workflowId,
        CancellationToken cancellationToken)
    {
        if (!IsValidId(workflowId))
        {
            return InvalidId();
        }

        var task = client.GetGrain<IWorkflowGrain>(workflowId).GetDurableTask<TResult>(RootId(kind, workflowId));
        DurableTaskResponse response;
        try
        {
            response = await task.GetResponseAsync(PollImmediately, cancellationToken);
        }
        catch (DurableTaskNotFoundException)
        {
            return Missing();
        }

        var taskId = task.Id.ToString();
        WorkflowStatusResponse status = response.Status switch
        {
            DurableTaskStatus.CompletedSuccessfully => new(taskId, "succeeded", response.GetResult<TResult>(), null),
            DurableTaskStatus.Canceled => new(taskId, "canceled", null, "Workflow was canceled."),
            DurableTaskStatus.Failed => new(taskId, "failed", null, "Workflow execution failed."),
            _ => new(taskId, "pending", null, null),
        };
        return Results.Ok(status);
    }

    private static IResult Accepted(string kind, string workflowId, TaskId taskId)
    {
        var location = $"/workflows/{kind}/{Uri.EscapeDataString(workflowId)}/status";
        return Results.Accepted(location, new WorkflowAcceptedResponse(taskId.ToString(), location));
    }

    private static IResult InvalidId() =>
        Results.BadRequest(new WorkflowApiError("Workflow IDs must be non-empty task-ID segments without '/' and cannot begin with '$'."));

    private static IResult InvalidItems() =>
        Results.BadRequest(new WorkflowApiError("A fan-out request must list at least one item."));

    private static IResult InvalidSubject() =>
        Results.BadRequest(new WorkflowApiError("Approval subjects must contain a non-whitespace value."));

    private static IResult Conflict() =>
        Results.Conflict(new WorkflowApiError("The workflow already recorded a different decision, or its deadline has passed."));

    private static IResult Missing() =>
        Results.NotFound(new WorkflowApiError("The workflow was not found or its retained result has expired."));

    private static bool IsValidId(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Contains('/') && !value.StartsWith('$');

    private static string RootId(string kind, string workflowId) => $"{kind}-{workflowId}";
}

public sealed record ApprovalSubmission(string Subject, bool Approved, string Reason);
public sealed record WorkflowAcceptedResponse(string TaskId, string StatusUrl);
public sealed record WorkflowStatusResponse(string TaskId, string Status, object? Result, string? Error);
public sealed record WorkflowApiError(string Error);
