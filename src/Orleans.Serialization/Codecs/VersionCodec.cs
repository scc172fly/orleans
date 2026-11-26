using Orleans.Serialization.Serializers;
using System;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="Version"/>的序列器。
    /// </summary>
    [RegisterSerializer]
    public sealed class VersionCodec : GeneralizedReferenceTypeSurrogateCodec<Version, VersionSurrogate>
    {
        /// <summary>
        /// 初始化 <see cref="VersionCodec"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public VersionCodec(IValueSerializer<VersionSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc />
        public override Version ConvertFromSurrogate(ref VersionSurrogate surrogate)
        {
            int revision = surrogate.Revision;
            int build = surrogate.Build;

            // ArgumentOutOfRangeException 如果任何论点少于零,则将抛出
            // 如果在施工期间未界定构建和修订是 - 1
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
    /// <see cref="VersionCodec"/>的代用类型。
    /// </summary>
    [GenerateSerializer]
    public struct VersionSurrogate
    {
        /// <summary>
        /// 获取或设置主版本组件。
        /// </summary>
        /// <value>主版本组件。</value>
        [Id(0)]
        public int Major;

        /// <summary>
        /// 获取或设置次要版本组件。
        /// </summary>
        /// <value>次要版本组件。</value>
        [Id(1)]
        public int Minor;

        /// <summary>
        /// 获取或设置构建数字。
        /// </summary>
        /// <value>构建号。</value>
        [Id(2)]
        public int Build;

        /// <summary>
        /// 获取或设置修改。
        /// </summary>
        /// <value>修改。</value>
        [Id(3)]
        public int Revision;
    }
}
