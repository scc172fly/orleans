using Orleans.Serialization.Cloning;
using System;

namespace Orleans.Serialization.Serializers
{
    /// <summary>
    /// 提供访问编解码器、激活器和复制器的功能。
    /// </summary>
    public interface ICodecProvider :
        IFieldCodecProvider,
        IBaseCodecProvider,
        IValueSerializerProvider,
        IActivatorProvider,
        IDeepCopierProvider
    {
        /// <summary>
        /// 获取服务提供程序。
        /// </summary>
        /// <value>服务提供程序。</value>
        IServiceProvider Services { get; }
    }
}
