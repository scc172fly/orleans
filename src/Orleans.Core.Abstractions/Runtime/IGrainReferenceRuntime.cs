using Orleans.CodeGeneration;
using Orleans.Serialization.Invocation;
using System;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// 使 <see cref="GrainReference"/> 可用的运行时逻辑。
    /// 此服务不应由用户代码直接使用。
    /// </summary>
    public interface IGrainReferenceRuntime
    {
        /// <summary>
        /// 在给定 Grain 接口上调用指定方法。
        /// </summary>
        /// <typeparam name="T">方法的底层返回类型。</typeparam>
        /// <param name="reference">Grain 引用。</param>
        /// <param name="request">方法描述。</param>
        /// <param name="options">调用选项。</param>
        /// <returns>调用结果。</returns>
        ValueTask<T?> InvokeMethodAsync<T>(GrainReference reference, IInvokable request, InvokeMethodOptions options);

        /// <summary>
        /// 在给定 Grain 接口上调用指定方法。
        /// </summary>
        /// <param name="reference">Grain 引用。</param>
        /// <param name="request">方法描述。</param>
        /// <param name="options">调用选项。</param>
        /// <returns>表示该操作的 <see cref="ValueTask"/>。</returns>
        ValueTask InvokeMethodAsync(GrainReference reference, IInvokable request, InvokeMethodOptions options);

        /// <summary>
        /// 在给定 Grain 接口上调用指定的返回 void 的方法，不等待响应。
        /// </summary>
        /// <param name="reference">Grain 引用。</param>
        /// <param name="request">方法描述。</param>
        /// <param name="options">调用选项。</param>
        void InvokeMethod(GrainReference reference, IInvokable request, InvokeMethodOptions options);

        /// <summary>
        /// 将提供的 <paramref name="grain"/> 转换为提供的 <paramref name="interfaceType"/>。
        /// </summary>
        /// <param name="grain">Grain。</param>
        /// <param name="interfaceType">转换所得的接口类型。</param>
        /// <returns>指向 <paramref name="grain"/> 且实现 <paramref name="interfaceType"/> 的引用。</returns>
        object Cast(IAddressable grain, Type interfaceType);
    }
}
