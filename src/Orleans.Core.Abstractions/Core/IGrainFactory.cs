using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 提供用于创建 Grain 引用的扩展方法。
    /// </summary>
    public static class GrainFactoryExtensions
    {
        /// <summary>
        /// 返回给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </summary>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="grainPrimaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">可选的类名前缀，用于查找 Grain 的运行时类型。</param>
        /// <typeparam name="TGrainInterface">返回的 Grain 引用必须实现的 Grain 接口类型。</typeparam>
        /// <returns>
        /// 给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </returns>
        public static TGrainInterface GetGrain<TGrainInterface>(this IGrainFactory grainFactory, IdSpan grainPrimaryKey, string grainClassNamePrefix) where TGrainInterface : IGrain
        {
            ArgumentNullException.ThrowIfNull(grainFactory);
            return grainFactory.GetGrain(typeof(TGrainInterface), grainPrimaryKey, grainClassNamePrefix).AsReference<TGrainInterface>();
        }

        /// <summary>
        /// 返回给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </summary>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="grainPrimaryKey">Grain 的主键。</param>
        /// <typeparam name="TGrainInterface">返回的 Grain 引用必须实现的 Grain 接口类型。</typeparam>
        /// <returns>
        /// 给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </returns>
        public static TGrainInterface GetGrain<TGrainInterface>(this IGrainFactory grainFactory, IdSpan grainPrimaryKey) where TGrainInterface : IGrain
        {
            ArgumentNullException.ThrowIfNull(grainFactory);
            return grainFactory.GetGrain(typeof(TGrainInterface), grainPrimaryKey).AsReference<TGrainInterface>();
        }
    }

    /// <summary>
    /// 提供创建 Grain 引用的功能。
    /// </summary>
    public interface IGrainFactory
    {
        /// <summary>
        /// 获取 Grain 的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">可选的类名前缀，用于查找 Grain 的运行时类型。</param>
        /// <returns>指定 Grain 的引用。</returns>
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey;

        /// <summary>
        /// 获取 Grain 的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">可选的类名前缀，用于查找 Grain 的运行时类型。</param>
        /// <returns>指定 Grain 的引用。</returns>
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey;

        /// <summary>
        /// 获取 Grain 的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">可选的类名前缀，用于查找 Grain 的运行时类型。</param>
        /// <returns>指定 Grain 的引用。</returns>
        TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey;

        /// <summary>
        /// 获取 Grain 的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="keyExtension">Grain 的扩展键。</param>
        /// <param name="grainClassNamePrefix">可选的类名前缀，用于查找 Grain 的运行时类型。</param>
        /// <returns>指定 Grain 的引用。</returns>
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey;

        /// <summary>
        /// 获取 Grain 的引用。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">Grain 的主键。</param>
        /// <param name="keyExtension">Grain 的扩展键。</param>
        /// <param name="grainClassNamePrefix">可选的类名前缀，用于查找 Grain 的运行时类型。</param>
        /// <returns>指定 Grain 的引用。</returns>
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey;

        /// <summary>
        /// 创建对给定 <paramref name="obj"/> 的引用。
        /// </summary>
        /// <typeparam name="TGrainObserverInterface">
        /// 具体的 <see cref="IGrainObserver"/> 类型，即 <paramref name="obj"/> 的类型。
        /// </typeparam>
        /// <param name="obj">要创建引用的对象。</param>
        /// <returns>对 <paramref name="obj"/> 的引用。</returns>
        TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver;

        /// <summary>
        /// 删除给定的对象引用。
        /// </summary>
        /// <typeparam name="TGrainObserverInterface">
        /// 具体的 <see cref="IGrainObserver"/> 类型，即 <paramref name="obj"/> 的类型。
        /// </typeparam>
        /// <param name="obj">要删除的引用。</param>
        /// <returns>表示所执行工作的 <see cref="Task"/>。</returns>
        void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver;

        /// <summary>
        /// 返回一个 Grain 的引用，该 Grain 是给定接口类型的主要实现，并具有给定主键。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 返回的 Grain 引用必须实现的 Grain 接口类型。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// Grain 的主键。
        /// </param>
        /// <returns>
        /// 给定接口类型的主要实现且具有给定主键的 Grain 的引用。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey);

        /// <summary>
        /// 返回一个 Grain 的引用，该 Grain 是给定接口类型的主要实现，并具有给定主键。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 返回的 Grain 引用必须实现的 Grain 接口类型。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// Grain 的主键。
        /// </param>
        /// <returns>
        /// 给定接口类型的主要实现且具有给定主键的 Grain 的引用。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey);

        /// <summary>
        /// 返回一个 Grain 的引用，该 Grain 是给定接口类型的主要实现，并具有给定主键。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 返回的 Grain 引用必须实现的 Grain 接口类型。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// Grain 的主键。
        /// </param>
        /// <returns>
        /// 给定接口类型的主要实现且具有给定主键的 Grain 的引用。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey);

        /// <summary>
        /// 返回一个 Grain 的引用，该 Grain 是给定接口类型的主要实现，并具有给定主键。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 返回的 Grain 引用必须实现的 Grain 接口类型。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// Grain 的主键。
        /// </param>
        /// <param name="keyExtension">
        /// Grain 键的扩展部分。
        /// </param>
        /// <returns>
        /// 给定接口类型的主要实现且具有给定主键的 Grain 的引用。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension);

        /// <summary>
        /// 返回一个 Grain 的引用，该 Grain 是给定接口类型的主要实现，并具有给定主键。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 返回的 Grain 引用必须实现的 Grain 接口类型。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// Grain 的主键。
        /// </param>
        /// <param name="keyExtension">
        /// Grain 键的扩展部分。
        /// </param>
        /// <returns>
        /// 给定接口类型的主要实现且具有给定主键的 Grain 的引用。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension);

        /// <summary>
        /// 返回实现指定接口的指定 Grain 的引用。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <typeparam name="TGrainInterface">
        /// 返回的 Grain 引用必须实现的 Grain 接口类型。
        /// </typeparam>
        /// <returns>
        /// 实现指定接口的指定 Grain 的引用。
        /// </returns>
        TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable;

        /// <summary>
        /// 返回给定 Grain ID 对应的无类型引用。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <returns>
        /// 给定 Grain ID 对应的无类型引用。
        /// </returns>
        IAddressable GetGrain(GrainId grainId);

        /// <summary>
        /// 返回给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </summary>
        /// <param name="grainId">
        /// Grain ID。
        /// </param>
        /// <param name="interfaceType">
        /// 返回的 Grain 引用必须实现的接口类型。
        /// </param>
        /// <returns>
        /// 给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </returns>
        IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType);

        /// <summary>
        /// 返回给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </summary>
        /// <param name="interfaceType">返回的 Grain 引用必须实现的 Grain 接口类型。</param>
        /// <param name="grainKey">Grain 的主键。</param>
        /// <param name="grainClassNamePrefix">用于查找 Grain 运行时类型的类名前缀。</param>
        /// <returns>
        /// 给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </returns>
        IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string grainClassNamePrefix);

        /// <summary>
        /// 返回给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </summary>
        /// <param name="interfaceType">返回的 Grain 引用必须实现的 Grain 接口类型。</param>
        /// <param name="grainKey">Grain 的主键。</param>
        /// <returns>
        /// 给定 Grain ID 对应的、实现指定接口类型的 Grain 引用。
        /// </returns>
        IAddressable GetGrain(Type interfaceType, IdSpan grainKey);
    }
}
