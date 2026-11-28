namespace Orleans;

/// <summary>
/// 此枚举提供用于控制字段 ID 生成逻辑的选项。
/// </summary>
public enum GenerateFieldIds
{
    /// <summary>
    /// 仅序列化显式标记了字段 ID 的成员。这是默认行为。
    /// </summary>
    None,
    /// <summary>
    /// 字段 ID 将自动分配给满足条件的公共属性。属性必须具有可访问的 getter，并且具有可访问的 setter 或对应的构造函数参数，才满足条件。
    /// </summary>
    /// <remarks>
    /// 如果类型的任意成员显式标记了字段 ID，则自动禁用该类型的字段 ID 自动生成。
    /// </remarks>
    PublicProperties
}
