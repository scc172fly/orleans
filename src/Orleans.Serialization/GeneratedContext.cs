using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization;

/// <summary>
/// 提供可连号类型封闭图的汇编时间登记。
/// </summary>
/// <remarks>
/// 生成这类类型的一个类别,并覆盖 <c>ConfigureInner</c> ,以登记闭路码、复印机、
/// 以及服务工厂。
/// 应用 <see cref="GeneratedContextTypeAttribute{T}"/> 适用于部分衍生的类别生成
/// 这些登记源自其申报的根基及其序列依赖性。
/// 使用 <see cref="SerializerBuilderExtensions.AddGeneratedContext"/>注册其实例 。
/// 多个环境促成了相同的序列器配置。
/// </remarks>
public abstract class GeneratedContext : TypeManifestProviderBase
{
    /// <summary>
    /// 创建一个静态封闭持有者,解决施工期间的递归编码依赖。
    /// </summary>
    /// <typeparam name="T">序列型号</typeparam>
    /// <param name="provider">编码器提供者。</param>
    /// <returns>注册编码的持有人。</returns>
    protected static IFieldCodec<T> CreateCodecHolder<T>(ICodecProvider provider)
        => new ServiceCollectionExtensions.FieldCodecHolder<T>(provider);

    /// <summary>
    /// 创建一个静态封闭持有者,解决对初次使用的递归复印机依赖。
    /// </summary>
    /// <typeparam name="T">复制类型。</typeparam>
    /// <param name="provider">编码器提供者。</param>
    /// <returns>注册复印机的持单人。</returns>
    protected static IDeepCopier<T> CreateCopierHolder<T>(ICodecProvider provider)
        => new ServiceCollectionExtensions.CopierHolder<T>(provider);
}
