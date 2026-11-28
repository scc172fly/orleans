using System;

using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 内部 Grain 工厂接口。
    /// </summary>
    internal interface IInternalGrainFactory : IGrainFactory
    {
        /// <summary>
        /// 创建指向所提供对象的引用。
        /// </summary>
        /// <typeparam name="TGrainObserverInterface">接口类型。</typeparam>
        /// <param name="obj">对象。</param>
        /// <returns>指向所提供对象的引用。</returns>
        TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IAddressable obj)
            where TGrainObserverInterface : IAddressable;

        /// <summary>
        /// 获取指定系统目标的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">系统目标接口。</typeparam>
        /// <param name="grainType">目标的类型。</param>
        /// <param name="destination">目标 Silo。</param>
        /// <returns>指定系统目标的引用。</returns>
        TGrainInterface GetSystemTarget<TGrainInterface>(GrainType grainType, SiloAddress destination)
            where TGrainInterface : ISystemTarget;

        /// <summary>
        /// 获取指定系统目标的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">系统目标接口。</typeparam>
        /// <param name="grainId">目标 ID。</param>
        /// <returns>指定系统目标的引用。</returns>
        TGrainInterface GetSystemTarget<TGrainInterface>(GrainId grainId) where TGrainInterface : ISystemTarget;

        /// <summary>
        /// 将所提供的 <paramref name="grain"/> 强制转换为指定接口。
        /// </summary>
        /// <typeparam name="TGrainInterface">目标 Grain 接口类型。</typeparam>
        /// <param name="grain">要进行类型转换的 Grain 引用。</param>
        /// <returns>
        /// 指向 <paramref name="grain"/> 且实现 <typeparamref name="TGrainInterface"/> 的引用。
        /// </returns>
        TGrainInterface Cast<TGrainInterface>(IAddressable grain);

        /// <summary>
        /// 将提供的 <paramref name="grain"/> 强制转换为提供的 <paramref name="interfaceType"/>。
        /// </summary>
        /// <param name="grain">Grain。</param>
        /// <param name="interfaceType">转换所得的接口类型。</param>
        /// <returns>指向 <paramref name="grain"/> 且实现 <paramref name="interfaceType"/> 的引用。</returns>
        object Cast(IAddressable grain, Type interfaceType);
    }
}
