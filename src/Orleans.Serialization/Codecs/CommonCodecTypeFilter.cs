using System;
using System.CodeDom.Compiler;
using System.Linq;
using System.Reflection;

using Orleans.Metadata;

namespace Orleans.Serialization.Codecs;

/// <summary>
/// 定义通用的类型筛选操作。
/// </summary>
public class CommonCodecTypeFilter
{
    /// <summary>
    /// 如果提供的类型是框架类型或抽象类型，则返回 true。
    /// </summary>
    /// <param name="type">要检查的类型。</param>
    /// <returns><see langword="true"/>，如果该类型是框架类型或抽象类型；否则为 <see langword="false"/>。</returns>
    public static bool IsAbstractOrFrameworkType(Type type)
    {
        ArgumentNullExceptionPolyfill.ThrowIfNull(type);

        if (type.IsAbstract
            || type.GetCustomAttributes<GeneratedCodeAttribute>().Any(a => a.Tool!.Equals("OrleansCodeGen", StringComparison.Ordinal))
            || type.Assembly.GetCustomAttribute<FrameworkPartAttribute>() is not null)
        {
            return true;
        }

        return false;
    }
}
