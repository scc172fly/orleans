using System.Diagnostics.CodeAnalysis;
using Orleans.GrainDirectory;
using Orleans.Metadata;

namespace Orleans.Runtime.GrainDirectory
{
    /// <summary>
    /// 将 <see cref="IGrainDirectory"/> 实例与 <see cref="GrainType"/> 关联。
    /// </summary>
    public interface IGrainDirectoryResolver
    {
        /// <summary>
        /// 获取 <see cref="IGrainDirectory" /> 实例，它对应于指定的 <see cref="GrainType" />。
        /// </summary>
        /// <param name="grainType">Grain 的类型。</param>
        /// <param name="properties">属性集合。</param>
        /// <param name="grainDirectory">Grain 目录。</param>
        /// <returns>如果找到合适的 Grain 目录，则为 true；否则为 <see langword="false"/>。</returns>
        bool TryResolveGrainDirectory(GrainType grainType, GrainProperties? properties, [NotNullWhen(true)] out IGrainDirectory? grainDirectory);
    }
}
