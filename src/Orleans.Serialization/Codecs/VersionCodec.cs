using Orleans.Serialization.Serializers;
using System;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Version"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class VersionCodec : GeneralizedReferenceTypeSurrogateCodec<Version, VersionSurrogate>
    {
        /// <summary>
        /// 初始化 <see cref="VersionCodec"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public VersionCodec(IValueSerializer<VersionSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc />
        public override Version ConvertFromSurrogate(ref VersionSurrogate surrogate)
        {
            int revision = surrogate.Revision;
            int build = surrogate.Build;

            // 如果任何参数小于零，则抛出 ArgumentOutOfRangeException。
            // 如果构造时未定义 Build 和 Revision，则其值为 -1。
            if (revision != -1)
            {
                return new Version(surrogate.Major, surrogate.Minor, build, revision);
            }
            else if (build != -1)
            {
                return new Version(surrogate.Major, surrogate.Minor, build);
            }
            else
            {
                return new Version(surrogate.Major, surrogate.Minor);
            }
        }

        /// <inheritdoc />
        public override void ConvertToSurrogate(Version value, ref VersionSurrogate surrogate)
        {
            surrogate.Major = value.Major;
            surrogate.Minor = value.Minor;
            surrogate.Build = value.Build;
            surrogate.Revision = value.Revision;
        }
    }

    /// <summary>
    /// <see cref="VersionCodec"/> 的代理类型。
    /// </summary>
    [GenerateSerializer]
    public struct VersionSurrogate
    {
        /// <summary>
        /// 获取或设置主版本号。
        /// </summary>
        /// <value>主版本号。</value>
        [Id(0)]
        public int Major;

        /// <summary>
        /// 获取或设置次版本号。
        /// </summary>
        /// <value>次版本号。</value>
        [Id(1)]
        public int Minor;

        /// <summary>
        /// 获取或设置生成号。
        /// </summary>
        /// <value>生成号。</value>
        [Id(2)]
        public int Build;

        /// <summary>
        /// 获取或设置修订号。
        /// </summary>
        /// <value>修订号。</value>
        [Id(3)]
        public int Revision;
    }
}
