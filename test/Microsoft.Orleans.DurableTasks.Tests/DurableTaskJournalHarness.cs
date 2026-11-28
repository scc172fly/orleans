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
/// 在易失性日志存储之上，以选定的日志格式承载日志化持久化任务存储。
/// </summary>
/// <remarks>
/// 服务来自 <c>AddJournaling</c> 和 <c>AddDurableTasks</c>，因此日志编解码器、JSON 元数据和
/// Orleans 序列化器与 Silo 一致。每个激活实例为同一日志创建作用域及新的日志化状态管理器，
/// 从而使前一个激活实例写入的状态能够被下一个激活实例恢复。
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

        // 与 Silo 一样，其他区域的带键时钟（包括持久化作业时钟）默认使用不带键的时钟。
        silo.Services.AddKeyedSingleton<TimeProvider>(KeyedService.AnyKey, static (sp, _) => sp.GetRequiredService<TimeProvider>());
        silo.AddJournaling();
        silo.AddDurableTasks();

        // Silo 将持久化任务与日志化持久化作业组合，后者的注册也为
        // 日志管理器自身的流目录和退役跟踪器提供 JSON 元数据。
        silo.UseJournaledDurableJobs();
        silo.Services.AddSerializer(builder => builder.AddAssembly(typeof(DurableTaskJournalHarness).Assembly));
        silo.Services.Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = journalFormatKey);
        silo.Services.AddSingleton<IJournalStorageProvider>(StorageProvider);
        silo.Services.AddScoped<IJournaledStateManager>(services =>
            services.GetRequiredService<IJournaledStateManagerFactory>().CreateStandalone(_journalId));
        _services = silo.Services.BuildServiceProvider();
    }

    /// <summary>获取持久化任务状态所支持的日志格式。</summary>
    public static TheoryData<string> Formats => new() { Json, OrleansBinary };

    public string JournalFormatKey { get; }

    public JournalId JournalId => _journalId;

    public VolatileJournalStorageProvider StorageProvider { get; }

    public Serializer Serializer => _services.GetRequiredService<Serializer>();

    /// <summary>创建激活作用域，并从日志恢复其任务状态。</summary>
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

    /// <summary>创建激活作用域而不恢复其状态，以便调用方先注册状态。</summary>
    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    /// <summary>返回存储的日志上标记的日志格式键。</summary>
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
