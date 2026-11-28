using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System.Globalization;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="CultureInfo"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class CultureInfoCodec : GeneralizedReferenceTypeSurrogateCodec<CultureInfo, CultureInfoSurrogate>
    {
        /// <summary>
        /// 初始化 <see cref="CultureInfoCodec"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public CultureInfoCodec(IValueSerializer<CultureInfoSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override CultureInfo ConvertFromSurrogate(ref CultureInfoSurrogate surrogate) => new(surrogate.Name);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(CultureInfo value, ref CultureInfoSurrogate surrogate) => surrogate.Name = value.Name;
    }

    /// <summary>
    /// <see cref="CultureInfoCodec"/> 所使用的代理类型。
    /// </summary>
    [GenerateSerializer]
    public struct CultureInfoSurrogate
    {
        /// <summary>
        /// 获取或设置名称。
        /// </summary>
        /// <value>名称。</value>
        [Id(0)]
        public string Name;
    }

    [RegisterCopier]
    internal sealed class CultureInfoCopier : ShallowCopier<CultureInfo>, IDerivedTypeCopier { }
}
