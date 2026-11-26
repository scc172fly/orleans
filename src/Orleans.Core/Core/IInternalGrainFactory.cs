using System;

using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 内部 grain 工厂界面。
    /// </summary>
    internal interface IInternalGrainFactory : IGrainFactory
    {
        /// <summary>
        /// 创建所提供对象的引用。
        /// </summary>
        /// <typeparam name="TGrainObserverInterface">接口的界面。</typeparam>
        /// <param name="obj">对象。</param>
        /// <returns>提及所提供的物体。</returns>
        TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IAddressable obj)
            where TGrainObserverInterface : IAddressable;

        /// <summary>
        /// 获取指定系统目标的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">系统目标接口。</typeparam>
        /// <param name="grainType">目标类型。</param>
        /// <param name="destination">目的地 silo.</param>
        /// <returns>指定系统目标的引用。</returns>
        TGrainInterface GetSystemTarget<TGrainInterface>(GrainType grainType, SiloAddress destination)
            where TGrainInterface : ISystemTarget;

        /// <summary>
        /// 获取指定系统目标的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">系统目标接口。</typeparam>
        /// <param name="grainId">目标的代号。</param>
        /// <returns>指定系统目标的引用。</returns>
        TGrainInterface GetSystemTarget<TGrainInterface>(GrainId grainId) where TGrainInterface : ISystemTarget;

        /// <summary>
        /// 播种指定接口中提供的 <paramref name="grain"/>
        /// </summary>
        /// <typeparam name="TGrainInterface">目标 grain 接口类型。</typeparam>
        /// <param name="grain">下 grain 参考文献。</param>
        /// <returns>
        /// 提及 <paramref name="grain"/> 执行 <typeparamref name="TGrainInterface"/>.
        /// </returns>
        TGrainInterface Cast<TGrainInterface>(IAddressable grain);

        /// <summary>
        /// 播种提供给提供 <paramref name="grain"/> 的 <paramref name="interfaceType"/>。
        /// </summary>
        /// <param name="grain">下 grain.</param>
        /// <param name="interfaceType">由此生成的接口类型。</param>
        /// <returns>提及 <paramref name="grain"/> 执行 <paramref name="interfaceType"/>.</returns>
        object Cast(IAddressable grain, Type interfaceType);
    }
}
