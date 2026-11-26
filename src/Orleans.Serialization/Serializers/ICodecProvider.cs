using Orleans.Serialization.Cloning;
using System;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供访问编码器、活动器和复印机的功能。
    /// </summary>
    public interface ICodecProvider :
        IFieldCodecProvider,
        IBaseCodecProvider,
        IValueSerializerProvider,
        IActivatorProvider,
        IDeepCopierProvider
    {
        /// <summary>
        /// 得到了服务供应商。
        /// </summary>
        /// <value>服务供应商。</value>
        IServiceProvider Services { get; }
    }
}
