using System;

namespace Orleans.Versions.Compatibility
{
    /// <summary>
    /// grain 接口兼容性主任的功能。
    /// </summary>
    public interface ICompatibilityDirector
    {
        /// <summary>
        /// 如果当前接口版本与要求的版本兼容,则返回 <see langword="true"/> ,否则返回 <see langword="false"/> 。
        /// </summary>
        /// <param name="requestedVersion">所请求的接口版本。</param>
        /// <param name="currentVersion">当前可用的接口版本。</param>
        /// <returns><see langword="true"/> 如果当前接口版本与所要求的版本兼容,则以其他方式 <see langword="false"/> 。</returns>
        bool IsCompatible(ushort requestedVersion, ushort currentVersion);
    }

    /// <summary>
    /// 全部 grain 接口版本兼容性战略的基础类。
    /// </summary>
    [Serializable, SerializerTransparent]
    public abstract class CompatibilityStrategy
    {
    }
}
