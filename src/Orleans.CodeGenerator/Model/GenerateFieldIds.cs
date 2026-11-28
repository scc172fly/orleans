namespace Orleans.CodeGenerator.Model;

/// <summary>
/// 此枚举提供控制字段 ID 生成逻辑的选项。
/// </summary>
public enum GenerateFieldIds
{
    /// <summary>
    /// 仅序列化显式标注字段 ID 的成员。这是默认设置。
    /// </summary>
    None,
    /// <summary>
    /// 自动为符合条件的公共属性分配字段 ID。属性必须具有可访问的 getter，并且具有可访问的 setter 或对应的构造函数参数。
    /// </summary>
    /// <remarks>
    /// 如果类型的任何成员显式标注了字段 ID，则自动禁用该类型的自动字段 ID 生成。
    /// </remarks>
    PublicProperties
}
