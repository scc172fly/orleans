using System.Diagnostics.CodeAnalysis;
using Orleans.GrainDirectory;
using Orleans.Metadata;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 关联 <see cref="IGrainDirectory"/> 实例与 <see cref="GrainType"/>。
    /// </summary>
    public interface IGrainDirectoryResolver
    {
        /// <summary>
        /// 得到 <see cref="IGrainDirectory" /> 提供的情况 <see cref="GrainType" />.
        /// </summary>
        /// <param name="grainType">类型 grain.</param>
        /// <param name="properties">属性。</param>
        /// <param name="grainDirectory"> grain 目录。</param>
        /// <returns>如果找到一个适当的 grain 目录,则属真 <see langword="false"/> 。</returns>
        bool TryResolveGrainDirectory(GrainType grainType, GrainProperties? properties, [NotNullWhen(true)] out IGrainDirectory? grainDirectory);
    }
}
