using System;
using System.CodeDom.Compiler;
using System.Linq;
using System.Reflection;

using Orleans.Metadata;

namespace Orleans.Serialization.Codecs;

/// <summary>
/// 定义通用类型过滤操作。
/// </summary>
public class CommonCodecTypeFilter
{
    /// <summary>
    /// 如果所提供的类型是一个框架或抽象类型,则返回为真实。
    /// </summary>
    /// <param name="type">要检查的类型。</param>
    /// <returns><see langword="true"/> 如果该类型是框架或抽象类型,则 <see langword="false"/>.</returns>
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
