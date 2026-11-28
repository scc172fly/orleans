using Orleans.Runtime;

namespace Orleans;

/// <summary>
/// 提供检查 Grain 是否已在本地激活的方法。
/// </summary>
public interface ILocalActivationStatusChecker
{
    /// <summary>
    /// 如果指定 Grain 已在本地激活，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。
    /// </summary>
    /// <param name="grainId">要检查的 Grain 的标识符。</param>
    /// <returns>如果指定 Grain 已在本地激活，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
    bool IsLocallyActivated(GrainId grainId);
}
