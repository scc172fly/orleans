using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 将 <see cref="IdealizedReminderTable"/> 参考实现安装到 <see cref="InProcessTestCluster"/> 中。
/// </summary>
/// <remarks>
/// 集群内的每个 Silo 共享同一个参考实现实例，模拟进程内集群在各 Silo 间共享
/// 一个 Grain 目录和一张成员资格表的方式。共享单个实例使集群级
/// 提醒器行为能够被确定性地观测：每个 Silo 的提醒器服务均读写同一
/// 可观测状态，且测试控制项（阻塞门控、注入故障、中断模拟及冻结
/// 读取）统一作用于整个集群。
/// </remarks>
public static class ReminderTestKitClusterBuilderExtensions
{
    /// <summary>
    /// 安装共享的 <see cref="IdealizedReminderTable"/>，作为集群内每个 Silo 的提醒器表。
    /// </summary>
    /// <param name="builder">测试集群构建器。</param>
    /// <param name="table">要安装的现有参考实现；使用 <see langword="null"/> 创建新实例。</param>
    /// <param name="reminderTimeProvider">
    /// 可选的确定性时钟，注册为提醒器子系统的键控
    /// <see cref="TimeProvider"/>（<see cref="ReminderTimeProviderNames.Reminders"/>）。无关的 Silo 定时器仍
    /// 使用环境中已有的提供程序。
    /// </param>
    /// <param name="configureReminderOptions">可选的 <see cref="ReminderOptions"/> 后配置回调。</param>
    /// <returns>已安装的参考实现，它公开确定性测试控制项及内部状态观测能力。</returns>
    public static IdealizedReminderTable UseIdealizedReminderTable(
        this InProcessTestClusterBuilder builder,
        IdealizedReminderTable? table = null,
        TimeProvider? reminderTimeProvider = null,
        Action<ReminderOptions>? configureReminderOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var oracle = table ?? new IdealizedReminderTable();
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.UseIdealizedReminderTable(oracle);

            if (reminderTimeProvider is not null)
            {
                siloBuilder.Services.AddKeyedSingleton(ReminderTimeProviderNames.Reminders, reminderTimeProvider);
            }

            if (configureReminderOptions is not null)
            {
                siloBuilder.Services.PostConfigure(configureReminderOptions);
            }
        });

        return oracle;
    }

    /// <summary>
    /// 安装提供的 <see cref="IdealizedReminderTable"/>，作为此 Silo 的提醒器表。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="table">要安装的参考实现。</param>
    /// <returns>用于链式调用的 Silo 构建器。</returns>
    public static ISiloBuilder UseIdealizedReminderTable(this ISiloBuilder builder, IdealizedReminderTable table)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(table);

        builder.AddReminders();
        builder.Services.AddSingleton(table);
        builder.Services.AddSingleton<IReminderTable>(table);
        return builder;
    }
}
