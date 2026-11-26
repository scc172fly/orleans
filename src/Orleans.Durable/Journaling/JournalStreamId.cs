namespace Orleans.Durable.Journaling;

/// <summary>
/// 确定一个州。
/// </summary>
/// <param name="Value">基本身份值。</param>
public readonly record struct JournalStreamId(uint Value);
