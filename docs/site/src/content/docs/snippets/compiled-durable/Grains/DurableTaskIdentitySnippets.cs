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

// 树上显示生成的识别符号,即$Child-2,这是一个未命名的单独任务根号ID的来源。
#pragma warning disable ORLEANS0220
// <durable_tasks_identity_tree>
public sealed class ReportGrain(IDurableTaskManager tasks) : DurableGrain, IReportGrain
{
    public static readonly DurableSignal<bool> Approved = new("approved");

    private string ReportId => this.GetPrimaryKeyString();

    public static string PublishTaskId(string reportId) => $"publish-{reportId}";

    // 调用者在“ 发布 - {reportId}” 根号ID下启动此项任务, 由 DurableTask.Current.Id 报告。
    public async DurableTask<PublishReceipt> PublishAsync(string[] sections)
    {
        // {reportId}/version,一个有名的录音效果,作为 WithId(“转换”) ,指一个步骤。
        var version = DurableTask.RunInline("version", () => DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // 发布 -{reportId以1美元为单位的死刑执行
        var draftId = DurableTask.RunInline(Guid.NewGuid);

        // {reportId}/render/0, imecury/1 等: 命名的组合器输入, 按输入顺序排列。
        // 将调用电话存储在调用范围下,$caller/{reportGrainId}/{incarnation}/publish-{reportId}/render/{i},
        // 此处的{incarnation} 标明此期出版 -{reportId} 。
        await sections.Select(section => Renderer(section).RenderAsync(section)).WhenAll().WithId("render");

        // {reportId}/ check, 然后检查2美元, 3美元等等: 此运行重复的一名称 。
        foreach (var section in sections)
        {
            await Check(section, draftId).WithId("check");
        }

        // {reportId}/$ignal: 批准, 信号记录在引用的任务之下 。
        if (!await Approved.WaitAsync())
        {
            return new(DurableTask.Current.Id, default, default, default);
        }

        // 命名的分离任务: 根“ 存档 - {reportId档案上 grain。执行记录起始为
        // {reportId}/ $detach:{{archiveGrainId}}: 存档{reportId}},这是处理器的 Id 报告。
        var archive = await Archive().ArchiveAsync(ReportId, (int)version).Detached($"archive-{ReportId}")
            .ScheduleAsync();

        // 一项未命名的独立任务:其根代号来自此 grain、此任务和下一个未命名的行动,
        // "{reportGrainId/出版 -{reportId$Child -2" 钱
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
        // 根的ID来自报告的身份 所以重新审理的请求 达到同样的任务
        ScheduledTask<PublishReceipt> publish = await client.GetGrain<IReportGrain>(reportId)
            .PublishAsync(sections)
            .WithId(ReportGrain.PublishTaskId(reportId))
            .ScheduleAsync();

        // 外部的持久任务,把手的代号与 TargetTaskId 是根身份。
        Console.WriteLine($"{publish.Id} {publish.TargetTaskId}");
        return await publish;
    }
    // </durable_tasks_identity_client>

    // <durable_tasks_identity_lookup>
    public static async Task<DurableTaskStatus> GetArchiveStatusAsync(IClusterClient client, string reportId)
    {
        // 命名的根用其 grain 及其名称来标出,每个当事方都源于报告的身份。
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
        // 独立任务是归档 grain的根, 因此它的处理有一个地址 。
        var archive = await GrainFactory.GetGrain<IArchiveGrain>("archive")
            .ArchiveAsync(reportId, version: 1)
            .Detached()
            .ScheduleAsync();
        archive.TryGetAddress(out var address);

        // 同步尾巴: 地址与任务结果连接 。
        _archive.Value = address;
    }

    // 持久任务订阅存储的任务,而订阅则记录重播。
    public async DurableTask<bool> AwaitArchiveAsync()
    {
        var archive = DurableTask.RunInline("archive", () => _archive.Value);
        return archive is { } address && await GrainFactory.GetDurableTask<bool>(address).WithId("archived");
    }

    // 普通方法调查或通过同一地址取消存储的任务。
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
    // 返回 HTTP 呼叫器的地址: System.Text.Json 写 {"Target":"报告/q3","Id":"Publish-q3"}。
    public static async Task<DurableTaskAddress> StartPublishAsync(IClusterClient client, string reportId, string[] sections)
    {
        var publish = await client.GetGrain<IReportGrain>(reportId)
            .PublishAsync(sections)
            .WithId(ReportGrain.PublishTaskId(reportId))
            .ScheduleAsync();
        publish.TryGetAddress(out var address);
        return address;
    }

    // 以后的请求又将地址回送,发出信号、等待或取消任务。
    public static async Task<DurableSignalResult> ApproveAsync(IClusterClient client, DurableTaskAddress publish, bool approved) =>
        await client.GetGrain(publish.Target).TrySignalDurableTaskAsync(publish.Id, ReportGrain.Approved, approved);

    public static async Task<PublishReceipt> WaitAsync(IClusterClient client, DurableTaskAddress publish) =>
        await client.GetDurableTask<PublishReceipt>(publish);
    // </durable_tasks_identity_send>
}
