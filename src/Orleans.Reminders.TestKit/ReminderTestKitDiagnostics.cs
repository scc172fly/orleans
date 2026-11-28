using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 当提醒器表实现违反文档规定的符合性保证时抛出的异常。
/// </summary>
[Serializable]
[GenerateSerializer]
public sealed class ReminderConformanceException : Exception
{
    /// <summary>
    /// 初始化 <see cref="ReminderConformanceException"/> 类的新实例。
    /// </summary>
    public ReminderConformanceException() : base("A reminder table conformance guarantee was violated.")
    {
    }

    /// <summary>
    /// 初始化 <see cref="ReminderConformanceException"/> 类的新实例。
    /// </summary>
    /// <param name="message">结构化失败报告。</param>
    public ReminderConformanceException(string message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="ReminderConformanceException"/> 类的新实例。
    /// </summary>
    /// <param name="message">结构化失败报告。</param>
    /// <param name="innerException">底层异常。</param>
    public ReminderConformanceException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    [Obsolete]
    private ReminderConformanceException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
/// 构建每项提醒器符合性测试失败所携带的结构化失败报告。
/// </summary>
/// <remarks>
/// 报告始终标识提供程序、被违反的保证及失败操作。此外，已知时还
/// 包含：操作序列、提醒器标识及其均匀哈希码、预期和
/// 观测结果、当前、上一及提供的 ETag、待测试的哈希范围及所有权，以及
/// 失败时的调度计划或加载窗口状态。
/// </remarks>
public sealed class ReminderFailureReport
{
    private readonly List<KeyValuePair<string, string>> _details = [];
    private readonly string _provider;
    private readonly string _guarantee;
    private readonly string _operation;

    private ReminderFailureReport(string provider, string guarantee, string operation)
    {
        _provider = string.IsNullOrWhiteSpace(provider) ? "<unnamed-provider>" : provider;
        _guarantee = string.IsNullOrWhiteSpace(guarantee) ? "<unnamed-guarantee>" : guarantee;
        _operation = string.IsNullOrWhiteSpace(operation) ? "<unnamed-operation>" : operation;
    }

    /// <summary>
    /// 为指定提供程序、保证及操作创建报告。
    /// </summary>
    /// <param name="provider">待测试的提供程序。</param>
    /// <param name="guarantee">正在验证的符合性保证。</param>
    /// <param name="operation">产生观测结果的 <see cref="IReminderTable"/> 操作。</param>
    /// <returns>新报告。</returns>
    public static ReminderFailureReport Create(string provider, string guarantee, string operation)
        => new(provider, guarantee, operation);

    /// <summary>
    /// 记录提醒器标识及其均匀哈希码。
    /// </summary>
    /// <param name="grainId">Grain 标识符。</param>
    /// <param name="reminderName">提醒器名称。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithIdentity(GrainId grainId, string? reminderName)
        => Add("reminder", $"GrainId={grainId}, ReminderName={Format(reminderName)}, UniformHash={grainId.GetUniformHashCode()} (0x{grainId.GetUniformHashCode():X8})");

    /// <summary>
    /// 记录预期结果。
    /// </summary>
    /// <param name="expected">预期结果的说明。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithExpected(string expected) => Add("expected", expected);

    /// <summary>
    /// 记录观测结果。
    /// </summary>
    /// <param name="observed">观测结果的说明。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithObserved(string observed) => Add("observed", observed);

    /// <summary>
    /// 记录与失败有关的 ETag。
    /// </summary>
    /// <param name="current">当前认为已持久化的 ETag。</param>
    /// <param name="previous">最近一次写入替换的 ETag。</param>
    /// <param name="supplied">提供给失败操作的 ETag。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithETags(string? current, string? previous = null, string? supplied = null)
        => Add("etags", $"current={Format(current)}, previous={Format(previous)}, supplied={Format(supplied)}");

    /// <summary>
    /// 记录待测试的哈希范围，包括是否跨越零点。
    /// </summary>
    /// <param name="begin">下界（不含该端点）。</param>
    /// <param name="end">上界（包含该端点）。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithRange(uint begin, uint end)
        => Add("range", $"(begin, end] = ({begin}, {end}], wrapAround={(begin >= end).ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// 记录与失败有关的所有权哈希码。
    /// </summary>
    /// <param name="label">说明哈希集合的标签，例如 <c>expectedInRange</c>。</param>
    /// <param name="hashes">均匀哈希码。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithOwnership(string label, IEnumerable<uint> hashes)
        => Add($"ownership.{label}", string.Join(", ", hashes.Select(hash => hash.ToString(CultureInfo.InvariantCulture))));

    /// <summary>
    /// 记录持久化的调度计划。
    /// </summary>
    /// <param name="startAt">提醒器开始时间。</param>
    /// <param name="period">提醒器周期。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithSchedule(DateTime startAt, TimeSpan period)
        => Add("schedule", $"StartAt={startAt:O} (Kind={startAt.Kind}), Period={period}");

    /// <summary>
    /// 记录失败时的时间及窗口状态。
    /// </summary>
    /// <param name="now">测试观测到的当前时间。</param>
    /// <param name="window">相关窗口的长度。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithWindow(DateTime now, TimeSpan window)
        => Add("window", $"now={now:O}, window={window}, windowEnd={now + window:O}");

    /// <summary>
    /// 记录导致失败的操作序列。
    /// </summary>
    /// <param name="position">失败操作从 1 开始计数的位置。</param>
    /// <param name="operations">完整操作序列。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithSequence(int position, IEnumerable<string> operations)
        => Add("sequence", $"#{position} of [{string.Join(" -> ", operations)}]");

    /// <summary>
    /// 记录一项额外的具名详情。
    /// </summary>
    /// <param name="name">详情名称。</param>
    /// <param name="value">详情值。</param>
    /// <returns>此报告。</returns>
    public ReminderFailureReport WithDetail(string name, string? value) => Add(name, Format(value));

    /// <summary>
    /// 渲染结构化失败报告。
    /// </summary>
    /// <returns>渲染后的报告。</returns>
    public string Build()
    {
        var builder = new StringBuilder();
        builder.Append("Reminder conformance failure [provider=").Append(_provider)
            .Append(", guarantee=").Append(_guarantee)
            .Append(", operation=").Append(_operation)
            .Append(']');

        foreach (var detail in _details)
        {
            builder.AppendLine().Append("  ").Append(detail.Key).Append(": ").Append(detail.Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 创建携带此报告的异常。
    /// </summary>
    /// <param name="innerException">可选的底层异常。</param>
    /// <returns>异常。</returns>
    public ReminderConformanceException ToException(Exception? innerException = null)
        => new(Build(), innerException);

    /// <summary>
    /// 抛出携带此报告的异常。
    /// </summary>
    /// <param name="innerException">可选的底层异常。</param>
    /// <exception cref="ReminderConformanceException">始终抛出。</exception>
    public void Throw(Exception? innerException = null) => throw ToException(innerException);

    private ReminderFailureReport Add(string name, string value)
    {
        _details.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    private static string Format(string? value) => value is null ? "<null>" : $"'{value}'";
}
