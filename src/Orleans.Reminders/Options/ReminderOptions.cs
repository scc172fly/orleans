using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Orleans.Reminders;
using Orleans.Runtime;

namespace Orleans.Hosting;

/// <summary>
/// 提醒器服务的选项。
/// </summary>
public sealed class ReminderOptions
{
    /// <summary>
    /// 获取或设置提醒器允许的最短周期。
    /// </summary>
    /// <remarks>
    /// 高频提醒器会给生产系统带来风险。
    /// </remarks>
    public TimeSpan MinimumReminderPeriod { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.MinimumReminderPeriodMinutes);

    /// <summary>
    /// 获取或设置提醒器表的刷新间隔。
    /// </summary>
    /// <value>默认每 5 分钟刷新一次提醒器表。</value>
    public TimeSpan RefreshReminderListPeriod { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.RefreshReminderListPeriodMinutes);

    /// <summary>
    /// 获取或设置在提醒器下次触发之前将其加载到内存的提前时间。
    /// </summary>
    /// <remarks>
    /// 位于此时间窗口之外的提醒器仅保留在提醒器存储中。如果提醒器存储无法刷新的时长超过此窗口，
    /// 提醒可能延迟投递。增大此值可以容忍更长的存储服务中断，但会增加内存用量。
    /// </remarks>
    /// <value>默认在提醒器下次触发前 10 分钟加载。</value>
    public TimeSpan ReminderLoadingWindow { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.ReminderLoadingWindowMinutes);

    /// <summary>
    /// 获取或设置放弃初始化提醒器之前允许尝试的最长时间。
    /// </summary>
    /// <value>默认尝试初始化 5 分钟，之后放弃。</value>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.InitializationTimeoutMinutes);
}

/// <summary>
/// <see cref="ReminderOptions"/> 的验证器。
/// </summary>
internal sealed partial class ReminderOptionsValidator : IConfigurationValidator
{
    private readonly ILogger<ReminderOptionsValidator> logger;
    private readonly IOptions<ReminderOptions> options;

    /// <summary>
    /// 初始化 <see cref="ReminderOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="logger">
    /// 日志记录器。
    /// </param>
    /// <param name="reminderOptions">
    /// 提醒器选项。
    /// </param>
    public ReminderOptionsValidator(ILogger<ReminderOptionsValidator> logger, IOptions<ReminderOptions> reminderOptions)
    {
        this.logger = logger;
        options = reminderOptions;
    }

    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        if (options.Value.MinimumReminderPeriod < TimeSpan.Zero)
        {
            throw new OrleansConfigurationException($"{nameof(ReminderOptions)}.{nameof(ReminderOptions.MinimumReminderPeriod)} must not be less than {TimeSpan.Zero}");
        }

        if (options.Value.RefreshReminderListPeriod <= TimeSpan.Zero)
        {
            throw new OrleansConfigurationException($"{nameof(ReminderOptions)}.{nameof(ReminderOptions.RefreshReminderListPeriod)} must be greater than {TimeSpan.Zero}");
        }

        if (options.Value.ReminderLoadingWindow <= TimeSpan.Zero)
        {
            throw new OrleansConfigurationException($"{nameof(ReminderOptions)}.{nameof(ReminderOptions.ReminderLoadingWindow)} must be greater than {TimeSpan.Zero}");
        }

        if (options.Value.ReminderLoadingWindow < options.Value.RefreshReminderListPeriod)
        {
            throw new OrleansConfigurationException(
                $"{nameof(ReminderOptions)}.{nameof(ReminderOptions.ReminderLoadingWindow)} must not be less than "
                + $"{nameof(ReminderOptions)}.{nameof(ReminderOptions.RefreshReminderListPeriod)}");
        }

        if (options.Value.MinimumReminderPeriod.TotalMinutes < ReminderOptionsDefaults.MinimumReminderPeriodMinutes)
        {
            LogWarnFastReminderInterval(options.Value.MinimumReminderPeriod, ReminderOptionsDefaults.MinimumReminderPeriodMinutes);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        EventId = (int)RSErrorCode.RS_FastReminderInterval,
        Message = $"{nameof(ReminderOptions)}.{nameof(ReminderOptions.MinimumReminderPeriod)} is {{MinimumReminderPeriod}} (default {{MinimumReminderPeriodMinutes}}. High-Frequency reminders are unsuitable for production use."
    )]
    private partial void LogWarnFastReminderInterval(TimeSpan minimumReminderPeriod, uint minimumReminderPeriodMinutes);
}
