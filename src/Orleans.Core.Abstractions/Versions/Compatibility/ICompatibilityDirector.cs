using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// 提供 Grain 接口兼容性决策器的功能。
    /// </summary>
    public interface ICompatibilityDirector
    {
        /// <summary>
        /// 如果接口的当前版本与请求的版本兼容，则返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </summary>
        /// <param name="requestedVersion">请求的接口版本。</param>
        /// <param name="currentVersion">当前可用的接口版本。</param>
        /// <returns><see langword="true"/> 表示接口的当前版本与请求的版本兼容；否则为 <see langword="false"/>。</returns>
        bool IsCompatible(ushort requestedVersion, ushort currentVersion);
    }

    /// <summary>
    /// 所有 Grain 接口版本兼容性策略的基类。
    /// </summary>
    [Serializable, SerializerTransparent]
    public abstract class CompatibilityStrategy
    {
    }
}
