using Orleans;
using Orleans.Durable.Tasks;
using Orleans.Durable.Journaling;
using Orleans.Runtime;

namespace Documentation.Grains.DurableTasks;

// <durable_tasks_identity_contracts>
[GenerateSerializer, Immutable]
public sealed record PublishReceipt(TaskId Workflow, TaskId Archive, TaskId Copy, TaskId ArchiveOperation);

public interface IReportGrain : IGrainWithStringKey
{
    DurableTask<PublishReceipt> PublishAsync(string[] sections);

    Task<DurableSignalResult> ApproveAsync(bool approved);
}

public interface IRendererGrain : IGrainWithStringKey
{
    DurableTask<int> RenderAsync(string section);
}

public interface IArchiveGrain : IGrainWithStringKey
{
    DurableTask<bool> ArchiveAsync(string reportId, int version);
}
// </durable_tasks_identity_contracts>

// 任务树显示生成的标识符 $child-2，未命名分离任务的根 ID 由此派生。
#pragma warning disable ORLEANS0220
// <durable_tasks_identity_tree>
public sealed class ReportGrain(IDurableTaskManager tasks) : DurableGrain, IReportGrain
{
    public static readonly DurableSignal<bool> Approved = new("approved");

    private string ReportId => this.GetPrimaryKeyString();

    public static string PublishTaskId(string reportId) => $"publish-{reportId}";

    // 调用方以根 ID "publish-{reportId}" 启动此任务，DurableTask.Current.Id 返回此 ID。
    public async DurableTask<PublishReceipt> PublishAsync(string[] sections)
    {
        // publish-{reportId}/version 是具名的已记录效果，类似 WithId("version") 为步骤命名。
        var version = DurableTask.RunInline("version", () => DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // publish-{reportId}/$child-1，即该执行实例的第一个未命名操作。
        var draftId = DurableTask.RunInline(Guid.NewGuid);

        // publish-{reportId}/render/0、render/1 等是具名组合器按输入顺序排列的输入。每个渲染器
        // 在调用方作用域 $caller/{reportGrainId}/{incarnation}/publish-{reportId}/render/{i} 下存储其调用，
        // 其中 {incarnation} 标识 publish-{reportId} 的本次运行。
        await sections.Select(section => Renderer(section).RenderAsync(section)).WhenAll().WithId("render");

        // publish-{reportId}/check，随后是 check$2、check$3 等：本次运行反复使用的同一个名称。
        foreach (var section in sections)
        {
            await Check(section, draftId).WithId("check");
        }

        // publish-{reportId}/$signal:approved 是被调用任务下的信号记录。
        if (!await Approved.WaitAsync())
        {
            return new(DurableTask.Current.Id, default, default, default);
        }

        // 具名分离任务：归档 Grain 上的根任务 "archive-{reportId}"。执行实例将启动记录为
        // publish-{reportId}/$detach:{archiveGrainId}:archive-{reportId}，由句柄的 Id 返回。
        var archive = await Archive().ArchiveAsync(ReportId, (int)version).Detached($"archive-{ReportId}")
            .ScheduleAsync();

        // 未命名的分离任务：根 ID 派生自当前 Grain、当前任务以及下一个未命名操作，
        // 即 "{reportGrainId}/publish-{reportId}:$child-2"。
        var copy = Archive().ArchiveAsync($"{ReportId}-copy", (int)version).Detached().Post();

        return new(DurableTask.Current.Id, archive.TargetTaskId, copy.TargetTaskId, archive.Id);
    }

    public async Task<DurableSignalResult> ApproveAsync(bool approved) =>
        await tasks.TrySignalAsync(PublishTaskId(ReportId), Approved, approved);

    private static async DurableTask<bool> Check(string section, Guid draftId) => section.Length > 0 && draftId != Guid.Empty;

    private IRendererGrain Renderer(string section) => GrainFactory.GetGrain<IRendererGrain>(section);

    private IArchiveGrain Archive() => GrainFactory.GetGrain<IArchiveGrain>("archive");
}
// </durable_tasks_identity_tree>
#pragma warning restore ORLEANS0220

internal static class ReportClient
{
    // <durable_tasks_identity_client>
    public static async Task<PublishReceipt> PublishAsync(IClusterClient client, string reportId, string[] sections)
    {
        // 根 ID 来自报表的标识，因此重试的请求会到达同一个任务。
        ScheduledTask<PublishReceipt> publish = await client.GetGrain<IReportGrain>(reportId)
            .PublishAsync(sections)
            .WithId(ReportGrain.PublishTaskId(reportId))
            .ScheduleAsync();

        // 在持久化任务之外，句柄的 Id 和 TargetTaskId 都是根 ID。
        Console.WriteLine($"{publish.Id} {publish.TargetTaskId}");
        return await publish;
    }
    // </durable_tasks_identity_client>

    // <durable_tasks_identity_lookup>
    public static async Task<DurableTaskStatus> GetArchiveStatusAsync(IClusterClient client, string reportId)
    {
        // 通过其 Grain 和名称寻址具名根任务，各方均从报表标识派生该名称。
        var archive = client.GetGrain<IArchiveGrain>("archive").GetDurableTask<bool>($"archive-{reportId}");
        return await archive.GetStatusAsync(new PollingOptions { PollTimeout = TimeSpan.Zero });
    }
    // </durable_tasks_identity_lookup>
}

// <durable_tasks_identity_store>
public interface IPublisherGrain : IGrainWithStringKey
{
    DurableTask HandOffAsync(string reportId);

    DurableTask<bool> AwaitArchiveAsync();

    Task<DurableTaskStatus?> GetArchiveStatusAsync();

    Task CancelArchiveAsync(string reason);
}

public sealed class PublisherGrain : DurableGrain, IPublisherGrain
{
    private readonly IDurableValue<DurableTaskAddress?> _archive;

    public PublisherGrain() => _archive = StateManager.GetOrAddValue<DurableTaskAddress?>("archive");

    public async DurableTask HandOffAsync(string reportId)
    {
        // 分离任务是归档 Grain 上的根任务，因此其句柄具有地址。
        var archive = await GrainFactory.GetGrain<IArchiveGrain>("archive")
            .ArchiveAsync(reportId, version: 1)
            .Detached()
            .ScheduleAsync();
        archive.TryGetAddress(out var address);

        // 末尾的同步代码：地址与当前任务的结果一同提交。
        _archive.Value = address;
    }

    // 持久化任务订阅已存储的任务，并记录该订阅以供重放使用。
    public async DurableTask<bool> AwaitArchiveAsync()
    {
        var archive = DurableTask.RunInline("archive", () => _archive.Value);
        return archive is { } address && await GrainFactory.GetDurableTask<bool>(address).WithId("archived");
    }

    // 普通方法通过相同地址轮询或取消已存储的任务。
    public async Task<DurableTaskStatus?> GetArchiveStatusAsync() =>
        _archive.Value is { } address
            ? await GrainFactory.GetDurableTask<bool>(address).GetStatusAsync(new PollingOptions { PollTimeout = TimeSpan.Zero })
            : null;

    public async Task CancelArchiveAsync(string reason)
    {
        if (_archive.Value is { } address)
        {
            await GrainFactory.GetDurableTask<bool>(address).CancelAsync(reason);
        }
    }
}
// </durable_tasks_identity_store>

internal static class ReportApi
{
    // <durable_tasks_identity_send>
    // 将地址返回给 HTTP 调用方：System.Text.Json 写入 {"Target":"report/q3","Id":"publish-q3"}。
    public static async Task<DurableTaskAddress> StartPublishAsync(IClusterClient client, string reportId, string[] sections)
    {
        var publish = await client.GetGrain<IReportGrain>(reportId)
            .PublishAsync(sections)
            .WithId(ReportGrain.PublishTaskId(reportId))
            .ScheduleAsync();
        publish.TryGetAddress(out var address);
        return address;
    }

    // 后续请求携带此地址，并向任务发送信号、等待或取消任务。
    public static async Task<DurableSignalResult> ApproveAsync(IClusterClient client, DurableTaskAddress publish, bool approved) =>
        await client.GetGrain(publish.Target).TrySignalDurableTaskAsync(publish.Id, ReportGrain.Approved, approved);

    public static async Task<PublishReceipt> WaitAsync(IClusterClient client, DurableTaskAddress publish) =>
        await client.GetDurableTask<PublishReceipt>(publish);
    // </durable_tasks_identity_send>
}
