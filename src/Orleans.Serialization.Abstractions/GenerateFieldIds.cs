namespace Orleans;

/// <summary>
/// 这个元件提供了控制字段代代逻辑的选项。
/// </summary>
public enum GenerateFieldIds
{
    /// <summary>
    /// 只有带有字段id的明确说明成员才会被序列。 这是默认的 。
    /// </summary>
    None,
    /// <summary>
    /// 字段代号将自动指定给合格的公共财产。 要获得资格, 产权必须有一个无障碍的获取器, 以及一个无障碍的设置器或相应的构建参数 。
    /// </summary>
    /// <remarks>
    /// 任何类型成员上存在明确的实地识别注释,将自动使该类型的自动字段代号产生失效。
    /// </remarks>
    PublicProperties
}
