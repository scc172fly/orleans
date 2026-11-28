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
    /// <summary>添加客户端的持久化 Grain RPC 适配器。</summary>
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

    /// <summary>添加基于 Orleans Messaging 的 Silo 端持久化 Grain RPC 运行时。</summary>
    /// <remarks>
    /// 消息传递依赖持久化作业（Durable Jobs），后者负责唤醒具有待处理消息的 Grain，包括计划的延迟恢复消息。
    /// 每个派生自 <see cref="Orleans.Durable.Journaling.DurableGrain"/> 的 Grain 都会获得收件箱和发件箱，
    /// 并在激活设置期间、日志恢复之前绑定运行时，且能够注入其
    /// <see cref="IDurableTaskManager"/>。任务状态存放在 Grain 的日志中，与日志格式无关；
    /// 此方法注册 JSON 日志格式所需的源生成 JSON 元数据，用于任务状态以及
    /// Grain 写入日志的 <see cref="DurableTaskAddress"/> 值。
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
