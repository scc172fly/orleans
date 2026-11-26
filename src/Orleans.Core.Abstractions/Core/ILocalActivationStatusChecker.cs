using Orleans.Runtime;

namespace Orleans;

/// <summary>
/// 提供一种方法检查 grain 是否本地激活。
/// </summary>
public interface ILocalActivationStatusChecker
{
    /// <summary>
    /// 返回 <see langword="true"/> 如果已提供 grain 本地激活; 否则, <see langword="false"/>.
    /// </summary>
    /// <param name="grainId">识别符号 grain 检查。</param>
    /// <returns><see langword="true"/> ,如果所提供的 grain 是本地激活的;否则, <see langword="false"/>。</returns>
    bool IsLocallyActivated(GrainId grainId);
}
