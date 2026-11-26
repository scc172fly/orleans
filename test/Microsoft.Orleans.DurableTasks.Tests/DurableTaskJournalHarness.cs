using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Durable.Tasks.Storage;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Serialization;
using Xunit;

namespace Microsoft.Orleans.DurableTasks.Tests;

/// <summary>
/// 主机以选定的日记格式, 编辑了持久任务存储,
/// </summary>
/// <remarks>
/// 服务来自 <c>AddJournaling</c> 和 <c>AddDurableTasks</c>,因此日记编码、 JSON 元数据以及
/// Orleans 序列器匹配 silo。每次激活都为该台创建了范围,由新日记的国家经理负责
/// 同一日记, 所以一个激活的状态 会在下一时间恢复
/// </remarks>
internal sealed class DurableTaskJournalHarness : IAsyncDisposable
{
    public const string Json = "json";
    public const string OrleansBinary = "orleans-binary";

    private readonly JournalId _journalId = JournalId.Create("durable-task-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;

    public DurableTaskJournalHarness(string journalFormatKey, TimeProvider? timeProvider = null)
    {
        JournalFormatKey = journalFormatKey;
        StorageProvider = new VolatileJournalStorageProvider(
            Options.Create(new JournaledStateManagerOptions { JournalFormatKey = journalFormatKey }));
        var silo = new HarnessSiloBuilder();
        silo.Services.AddLogging();
        silo.Services.AddSingleton(timeProvider ?? TimeProvider.System);
        silo.Services.AddKeyedSingleton(JournalingTimeProviderNames.Journaling, TimeProvider.System);

        // 类似 silo,其它区域的键盘钟,包括“持久工作钟”,默认为无锁的钟。
        silo.Services.AddKeyedSingleton<TimeProvider>(KeyedService.AnyKey, static (sp, _) => sp.GetRequiredService<TimeProvider>());
        silo.AddJournaling();
        silo.AddDurableTasks();

        // 一个 silo 构成具有经期刊编辑的持久工作的长期任务,其登记也为以下目的提供 JSON 元数据:
        // 日记经理自己的流目录和退休跟踪器
        silo.UseJournaledDurableJobs();
        silo.Services.AddSerializer(builder => builder.AddAssembly(typeof(DurableTaskJournalHarness).Assembly));
        silo.Services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = journalFormatKey);
        silo.Services.AddSingleton<IJournalStorageProvider>(StorageProvider);
        silo.Services.AddScoped<IJournaledStateManager>(services =>
            services.GetRequiredService<IJournaledStateManagerFactory>().CreateStandalone(_journalId));
        _services = silo.Services.BuildServiceProvider();
    }

    /// <summary>获取耐久任务状态支持的日记格式。</summary>
    public static TheoryData<string> Formats => new() { Json, OrleansBinary };

    public string JournalFormatKey { get; }

    public JournalId JournalId => _journalId;

    public VolatileJournalStorageProvider StorageProvider { get; }

    public Serializer Serializer => _services.GetRequiredService<Serializer>();

    /// <summary>创建激活范围并从日记中恢复任务状态。</summary>
    public async Task<Activation> ActivateAsync(CancellationToken cancellationToken)
    {
        var scope = _services.CreateAsyncScope();
        try
        {
            var storage = scope.ServiceProvider.GetRequiredService<DurableTaskGrainStorage>();
            await storage.ReadAsync(cancellationToken);
            return new(scope, storage);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    /// <summary>创建启动范围,而不恢复状态,因此呼叫者可以先登记国家。</summary>
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    /// <summary>返回存储在日记上标记的日记格式密钥。</summary>
    public async Task<string?> GetStoredJournalFormatKeyAsync(CancellationToken cancellationToken)
    {
        var metadata = await StorageProvider.CreateStorage(_journalId).GetMetadataAsync(cancellationToken);
        return metadata?.FormatKey;
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    public sealed class Activation(AsyncServiceScope scope, DurableTaskGrainStorage storage) : IAsyncDisposable
    {
        public DurableTaskGrainStorage Storage { get; } = storage;

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private sealed class HarnessSiloBuilder : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
    }
}
