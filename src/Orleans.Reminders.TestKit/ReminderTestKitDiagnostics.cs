using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Orleans.Runtime;

namespace Orleans.Reminders.TestKit;

/// <summary>
/// 执行提醒表违反文件记录的遵守保证时提出的例外。
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
    /// <param name="message">结构化故障报告。</param>
    public ReminderConformanceException(string message) : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="ReminderConformanceException"/> 类的新实例。
    /// </summary>
    /// <param name="message">结构化故障报告。</param>
    /// <param name="innerException">基本例外。</param>
    public ReminderConformanceException(string message, Exception? innerException) : base(message, innerException)
    {
    }

    [Obsolete]
    private ReminderConformanceException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
/// 构建每条提醒提示不合规都携带的结构性故障报告。
/// </summary>
/// <remarks>
/// 报告总是指明提供者、被违反的担保和失败的操作。
/// 已知时,即携带:操作序列、提醒身份及其统一的散列代码、预期和
/// 观测结果、当前、过去和供应的ETags、散列范围以及正在测试的所有权,
/// 计划或负载窗口在失败点的状态。
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
    /// 为指定的提供者、 担保和操作创建报告。
    /// </summary>
    /// <param name="provider">供应商正在接受测试。</param>
    /// <param name="guarantee">合规保证正在核实。</param>
    /// <param name="operation">下 <see cref="IReminderTable"/> 其操作产生观察结果。</param>
    /// <returns>新报告。</returns>
    public static ReminderFailureReport Create(string provider, string guarantee, string operation)
        => new(provider, guarantee, operation);

    /// <summary>
    /// 记录提醒身份及其统一的散列代码。
    /// </summary>
    /// <param name="grainId"> grain 标识符。</param>
    /// <param name="reminderName">提醒的名称。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithIdentity(GrainId grainId, string? reminderName)
        => Add("reminder", $"GrainId={grainId}, ReminderName={Format(reminderName)}, UniformHash={grainId.GetUniformHashCode()} (0x{grainId.GetUniformHashCode():X8})");

    /// <summary>
    /// 记录预期成果。
    /// </summary>
    /// <param name="expected">说明预期成果。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithExpected(string expected) => Add("expected", expected);

    /// <summary>
    /// 记录观察到的结果。
    /// </summary>
    /// <param name="observed">说明观察到的结果。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithObserved(string observed) => Add("observed", observed);

    /// <summary>
    /// 记录与失败有关的ETags。
    /// </summary>
    /// <param name="current">据信目前仍然存在的ETag。</param>
    /// <param name="previous">以最近写作取代的ETag。</param>
    /// <param name="supplied">ETag供应给失败的操作。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithETags(string? current, string? previous = null, string? supplied = null)
        => Add("etags", $"current={Format(current)}, previous={Format(previous)}, supplied={Format(supplied)}");

    /// <summary>
    /// 记录试验中的散列范围,包括是否在零周围。
    /// </summary>
    /// <param name="begin">排他性下限约束</param>
    /// <param name="end">包含式上约束。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithRange(uint begin, uint end)
        => Add("range", $"(begin, end] = ({begin}, {end}], wrapAround={(begin >= end).ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// 记录与故障有关的所有权散列码。
    /// </summary>
    /// <param name="label">标签,例如 <c>expectedInRange</c>。</param>
    /// <param name="hashes">统一的散列代码。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithOwnership(string label, IEnumerable<uint> hashes)
        => Add($"ownership.{label}", string.Join(", ", hashes.Select(hash => hash.ToString(CultureInfo.InvariantCulture))));

    /// <summary>
    /// 记录现行时间表。
    /// </summary>
    /// <param name="startAt">提醒开始时间。</param>
    /// <param name="period">提醒期。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithSchedule(DateTime startAt, TimeSpan period)
        => Add("schedule", $"StartAt={startAt:O} (Kind={startAt.Kind}), Period={period}");

    /// <summary>
    /// 记录失败点的时间和窗口状态。
    /// </summary>
    /// <param name="now">试验所观察到的当前时间。</param>
    /// <param name="window">相关窗口长度。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithWindow(DateTime now, TimeSpan window)
        => Add("window", $"now={now:O}, window={window}, windowEnd={now + window:O}");

    /// <summary>
    /// 记录产生故障的操作序列。
    /// </summary>
    /// <param name="position">运行失败的单一位置。</param>
    /// <param name="operations">整个运行序列。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithSequence(int position, IEnumerable<string> operations)
        => Add("sequence", $"#{position} of [{string.Join(" -> ", operations)}]");

    /// <summary>
    /// 记录一个附加的指定细节。
    /// </summary>
    /// <param name="name">详细名称。</param>
    /// <param name="value">详细值。</param>
    /// <returns>本报告。</returns>
    public ReminderFailureReport WithDetail(string name, string? value) => Add(name, Format(value));

    /// <summary>
    /// 重审结构化故障报告
    /// </summary>
    /// <returns>所提报告。</returns>
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
    /// 创建包含此报表的例外。
    /// </summary>
    /// <param name="innerException">一种任择性的基本例外。</param>
    /// <returns>例外。</returns>
    public ReminderConformanceException ToException(Exception? innerException = null)
        => new(Build(), innerException);

    /// <summary>
    /// 将包含本报告的例外抛出。
    /// </summary>
    /// <param name="innerException">一种任择性的基本例外。</param>
    /// <exception cref="ReminderConformanceException">总是抛出。</exception>
    public void Throw(Exception? innerException = null) => throw ToException(innerException);

    private ReminderFailureReport Add(string name, string value)
    {
        _details.Add(new KeyValuePair<string, string>(name, value));
        return this;
    }

    private static string Format(string? value) => value is null ? "<null>" : $"'{value}'";
}
