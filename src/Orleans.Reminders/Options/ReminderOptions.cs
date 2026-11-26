using System;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Orleans.Reminders;
using Orleans.Runtime;

namespace Orleans.Hosting;

/// <summary>
/// 提醒服务选项 。
/// </summary>
public sealed class ReminderOptions
{
    /// <summary>
    /// 获取或设定提醒的最低期限。
    /// </summary>
    /// <remarks>
    /// 高频提醒对生产系统有危险。
    /// </remarks>
    public TimeSpan MinimumReminderPeriod { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.MinimumReminderPeriodMinutes);

    /// <summary>
    /// 获取或设置提醒表格刷新间隔时间。
    /// </summary>
    /// <value>默认情况下每5分钟刷新提醒表。</value>
    public TimeSpan RefreshReminderListPeriod { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.RefreshReminderListPeriodMinutes);

    /// <summary>
    /// 获取或设定下次选中时的间隔时间, 提醒会被装入内存 。
    /// </summary>
    /// <remarks>
    /// 此窗口外的提醒仅保留在提醒存储中。 如果提醒存储无法更新比此窗口更长的时间, 则无法刷新, @ info: whatsthis
    /// 提醒可以迟发。 增加这一值,以容忍储存时间更长的中断, 成本是存储使用率提高。
    /// </remarks>
    /// <value>提醒在下次选中之前10分钟装入。</value>
    public TimeSpan ReminderLoadingWindow { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.ReminderLoadingWindowMinutes);

    /// <summary>
    /// 获取或设定最大时间, 以尝试在放弃前先启动提醒 。
    /// </summary>
    /// <value>试图在默认放弃前先初始化5分钟。</value>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromMinutes(ReminderOptionsDefaults.InitializationTimeoutMinutes);
}

/// <summary>
/// <see cref="ReminderOptions"/>的校验符。
/// </summary>
internal sealed partial class ReminderOptionsValidator : IConfigurationValidator
{
    private readonly ILogger<ReminderOptionsValidator> logger;
    private readonly IOptions<ReminderOptions> options;

    /// <summary>
    /// 初始化 <see cref="ReminderOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="logger">
    /// 伐木工
    /// </param>
    /// <param name="reminderOptions">
    /// 提醒选项。
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
