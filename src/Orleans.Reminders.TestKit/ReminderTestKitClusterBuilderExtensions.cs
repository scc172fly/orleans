using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 将 <see cref="IdealizedReminderTable"/> 或“关卡”安装到“ <see cref="InProcessTestCluster"/>”。
/// </summary>
/// <remarks>
/// 组合体中每个 silo 都共享一个单一的甲骨文实例,这反映了流程中的集束份额的方式
/// 单 grain 和成员表 silos共享一个实例,就是集群一级
/// 明确可见的提醒行为:每个 silo提醒服务读写相同
/// 内可预见状态和试验控制(阻挡门、注射失败、模拟停机和冻结)
/// 在整个组别中统一适用。
/// </remarks>
public static class ReminderTestKitClusterBuilderExtensions
{
    /// <summary>
    /// 安装一个共享的 <see cref="IdealizedReminderTable"/> 作为集件中每个 silo 的提醒表。
    /// </summary>
    /// <param name="builder">测试组群构建器。</param>
    /// <param name="table">安装的已有信箱,或创建一个 <see langword="null"/> 。</param>
    /// <param name="reminderTimeProvider">
    /// 以提醒子系统密钥注册的可选定时表
    /// <see cref="TimeProvider"/> (<see cref="ReminderTimeProviderNames.Reminders"/>无关 silo 时间间隔
    /// 使用环境提供者。
    /// </param>
    /// <param name="configureReminderOptions">任选 <see cref="ReminderOptions"/> 后配置回调 。</param>
    /// <returns>安装的触角,它暴露了确定性测试控制和反省。</returns>
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
    /// 安装所提供 <see cref="IdealizedReminderTable"/> 作为 silo的提醒表格。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="table">装设神器</param>
    /// <returns>用于链条的 silo 构建器。</returns>
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
