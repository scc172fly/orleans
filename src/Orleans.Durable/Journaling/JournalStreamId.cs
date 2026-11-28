namespace Orleans.Durable.Journaling;

/// <summary>
/// 标识一个状态。
/// </summary>
/// <param name="Value">底层标识值。</param>
public readonly record struct JournalStreamId(uint Value);
