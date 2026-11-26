using Orleans.CodeGeneration;
using Orleans.Serialization.Invocation;
using System;
using System.Threading.Tasks;

namespace Orleans.Runtime
{
    /// <summary>
    /// <see cref="GrainReference"/>s 的运行时逻辑可用 。
    /// 这一服务不是直接用用户代码来使用。
    /// </summary>
    public interface IGrainReferenceRuntime
    {
        /// <summary>
        /// 在提供的 grain 接口上启用指定方法。
        /// </summary>
        /// <typeparam name="T">方法的返回类型。</typeparam>
        /// <param name="reference">下 grain 参考。</param>
        /// <param name="request">方法说明。</param>
        /// <param name="options">援引选项。</param>
        /// <returns>援引的结果。</returns>
        ValueTask<T?> InvokeMethodAsync<T>(GrainReference reference, IInvokable request, InvokeMethodOptions options);

        /// <summary>
        /// 在提供的 grain 接口上启用指定方法。
        /// </summary>
        /// <param name="reference">下 grain 参考。</param>
        /// <param name="request">方法说明。</param>
        /// <param name="options">援引选项。</param>
        /// <returns>A <see cref="ValueTask"/> 代表作业</returns>
        ValueTask InvokeMethodAsync(GrainReference reference, IInvokable request, InvokeMethodOptions options);

        /// <summary>
        /// 在提供 grain 接口上不等待回复而启用指定的无效返回方法。
        /// </summary>
        /// <param name="reference">下 grain 参考。</param>
        /// <param name="request">方法说明。</param>
        /// <param name="options">援引选项。</param>
        void InvokeMethod(GrainReference reference, IInvokable request, InvokeMethodOptions options);

        /// <summary>
        /// 将提供的 <paramref name="grain"/> 转换为提供的 <paramref name="interfaceType"/>。
        /// </summary>
        /// <param name="grain">下 grain.</param>
        /// <param name="interfaceType">由此生成的接口类型。</param>
        /// <returns>提及 <paramref name="grain"/> 执行 <paramref name="interfaceType"/>.</returns>
        object Cast(IAddressable grain, Type interfaceType);
    }
}
