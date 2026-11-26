using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Durable.Tasks.Protocol;
using Orleans.Durable.Tasks.Runtime;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.Durable.Tasks;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;
using Orleans.Durable.Tasks.Storage;
using Orleans.Durable.Messaging;
using Orleans.Runtime;
using Orleans.Serialization.Configuration;

namespace Orleans.Hosting;

public static class DurableTaskHostingExtensions
{
    /// <summary>添加客户端耐用 grain RPC 适配器。</summary>
    public static IClientBuilder AddDurableTasks(this IClientBuilder clientBuilder)
    {
        clientBuilder.Services.TryAddSingleton<DurableTaskRequestShared>();
        clientBuilder.Configure<TypeManifestOptions>(
            options =>
            {
                options.AddAllowedAssembly(typeof(Orleans.Durable.Tasks.DurableTaskResponse).Assembly);
                options.AddAllowedAssembly(typeof(DurableTaskRequest).Assembly);
            });
        return clientBuilder;
    }

    /// <summary>添加 silo- 侧耐用 grain RPC 在 Orleans 通信期间的运行时间。</summary>
    /// <remarks>
    /// 通信需要持久工作,这唤醒 grains 的未了信息,包括计划推迟恢复的工作。
    /// 每一 grain 源自 <see cref="Orleans.Durable.Journaling.DurableGrain"/> 收受一个收发箱和一发发发箱,
    /// 并绑定启动安装期间的运行时间, 在日记恢复前, 并且可以注射
    /// <see cref="IDurableTaskManager"/>. 任务状态以任何日记格式在 grain日记中存在;
    /// 此方法将登记源生成的 JSON 元数据,而 JSON 日记格式要求该元数据是任务状态和任务状态所需的。
    /// 用于 <see cref="DurableTaskAddress"/> 数值的值 grains 日记。
    /// </remarks>
    public static ISiloBuilder AddDurableTasks(
        this ISiloBuilder siloBuilder,
        Action<DurableTaskOptions>? configureOptions = null)
    {
        siloBuilder.AddMessaging();
        siloBuilder.Services.Configure<JsonJournalOptions>(static options =>
        {
            options.AddTypeInfoResolver(DurableTaskJsonContext.Default);
        });
        var options = siloBuilder.Services.AddOptions<DurableTaskOptions>();
        if (configureOptions is not null)
        {
            options.Configure(configureOptions);
        }
        options.Validate(
            value => value.ResultRetentionPeriod >= TimeSpan.Zero,
            "Durable task result retention must not be negative.");
        options.Validate(
            value => value.StalledExecutionRetryInterval > TimeSpan.Zero,
            "The durable task stalled execution retry interval must be positive.");
        options.Validate(
            value => value.RunnableExecutionWakeupDelay > TimeSpan.Zero,
            "The durable task runnable execution wake-up delay must be positive.");
        siloBuilder.Services.TryAddSingleton<DurableTaskRequestShared>();
        siloBuilder.Configure<TypeManifestOptions>(
            options =>
            {
                options.AddAllowedAssembly(typeof(Orleans.Durable.Tasks.DurableTaskResponse).Assembly);
                options.AddAllowedAssembly(typeof(DurableTaskRequest).Assembly);
            });
        siloBuilder.Services.AddSingleton<DurableTaskGrainRuntimeShared>();
        siloBuilder.Services.TryAddSingleton<DurableTaskMessageTypes>();
        siloBuilder.Services.AddScoped<DurableTaskGrainRuntime>();
        siloBuilder.Services.AddFromExisting<IDurableTaskGrainRuntime, DurableTaskGrainRuntime>();
        siloBuilder.Services.AddFromExisting<IDurableTaskManager, DurableTaskGrainRuntime>();
        siloBuilder.Services.AddScoped(static services => new DurableTaskMessageTransport(
            new Lazy<IOutbox>(services.GetRequiredService<IOutbox>),
            services.GetRequiredService<DurableTaskMessageTypes>()));
        siloBuilder.Services.AddFromExisting<IDurableTaskMessageTransport, DurableTaskMessageTransport>();
        siloBuilder.Services.AddScoped<DurableTaskMessageHandler>();
        siloBuilder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureGrainTypeComponents, DurableTaskGrainTypeConfigurator>());
        siloBuilder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IActivationWorkingSetObserver, DurableTaskActivationObserver>());
        siloBuilder.Services.AddKeyedTransient<IGrainExtension>(typeof(IDurableTaskGrainExtension), (sp, _) => sp.GetRequiredService<DurableTaskGrainRuntime>());
        siloBuilder.Services.AddKeyedTransient<IGrainExtension>(typeof(IDurableTaskServer), (sp, _) => sp.GetRequiredService<DurableTaskGrainRuntime>());

        siloBuilder.Services.TryAddSingleton(TimeProvider.System);
        siloBuilder.Services.TryAddScoped<DurableTaskGrainStorage>();
        siloBuilder.Services.AddFromExisting<IDurableTaskGrainStorage, DurableTaskGrainStorage>();
        return siloBuilder;
    }
}
