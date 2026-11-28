using Orleans.Serialization.Cloning;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.Configuration;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization;

/// <summary>
/// 为序列化类型的封闭类型图提供编译时注册。
/// </summary>
/// <remarks>
/// 从此类型派生类并重写 <c>ConfigureInner</c>，以注册封闭的编解码器、复制器
/// 和服务工厂。
/// 将 <see cref="GeneratedContextTypeAttribute{T}"/> 应用于 partial 派生类，即可生成
/// 其声明的根类型及序列化依赖项的注册代码。
/// 使用 <see cref="SerializerBuilderExtensions.AddGeneratedContext"/> 注册其实例。
/// 多个上下文可以共同配置同一个序列化器。
/// </remarks>
public abstract class GeneratedContext : TypeManifestProviderBase
{
    /// <summary>
    /// 创建一个静态封闭的持有器，在构造期间解析递归编解码器依赖项。
    /// </summary>
    /// <typeparam name="T">序列化的类型。</typeparam>
    /// <param name="provider">编解码器提供程序。</param>
    /// <returns>已注册编解码器的持有器。</returns>
    protected static IFieldCodec<T> CreateCodecHolder<T>(ICodecProvider provider)
        => new ServiceCollectionExtensions.FieldCodecHolder<T>(provider);

    /// <summary>
    /// 创建一个静态封闭的持有器，在首次使用时解析递归复制器依赖项。
    /// </summary>
    /// <typeparam name="T">要复制的类型。</typeparam>
    /// <param name="provider">编解码器提供程序。</param>
    /// <returns>已注册复制器的持有器。</returns>
    protected static IDeepCopier<T> CreateCopierHolder<T>(ICodecProvider provider)
        => new ServiceCollectionExtensions.CopierHolder<T>(provider);
}
