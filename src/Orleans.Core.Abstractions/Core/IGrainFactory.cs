using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 提供创建 grain 引用的扩展方法。
    /// </summary>
    public static class GrainFactoryExtensions
    {
        /// <summary>
        /// 返回提供执行指定接口类型的 grain id的引用。
        /// </summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="grainPrimaryKey">关键是 grain</param>
        /// <param name="grainClassNamePrefix">一个可选类名前缀, 用于查找 grain运行时间类型。</param>
        /// <typeparam name="TGrainInterface">下 grain 返回的界面类型 grain 参考必须执行。</typeparam>
        /// <returns>
        /// 用于提供执行指定接口类型的 grain id的参考文献。
        /// </returns>
        public static TGrainInterface GetGrain<TGrainInterface>(this IGrainFactory grainFactory, IdSpan grainPrimaryKey, string grainClassNamePrefix) where TGrainInterface : IGrain
        {
            ArgumentNullException.ThrowIfNull(grainFactory);
            return grainFactory.GetGrain(typeof(TGrainInterface), grainPrimaryKey, grainClassNamePrefix).AsReference<TGrainInterface>();
        }

        /// <summary>
        /// 返回提供执行指定接口类型的 grain id的引用。
        /// </summary>
        /// <param name="grainFactory">下 grain 工厂。</param>
        /// <param name="grainPrimaryKey">关键是 grain</param>
        /// <typeparam name="TGrainInterface">下 grain 返回的界面类型 grain 参考必须执行。</typeparam>
        /// <returns>
        /// 用于提供执行指定接口类型的 grain id的参考文献。
        /// </returns>
        public static TGrainInterface GetGrain<TGrainInterface>(this IGrainFactory grainFactory, IdSpan grainPrimaryKey) where TGrainInterface : IGrain
        {
            ArgumentNullException.ThrowIfNull(grainFactory);
            return grainFactory.GetGrain(typeof(TGrainInterface), grainPrimaryKey).AsReference<TGrainInterface>();
        }
    }

    /// <summary>
    /// 创建 grains引用功能。
    /// </summary>
    public interface IGrainFactory
    {
        /// <summary>
        /// 获取引用 grain。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">关键是 grain.</param>
        /// <param name="grainClassNamePrefix">一个可选类名前缀, 用于查找 grain运行时间类型。</param>
        /// <returns>指指定的 grain。</returns>
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey;

        /// <summary>
        /// 获取引用 grain。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">关键是 grain.</param>
        /// <param name="grainClassNamePrefix">一个可选类名前缀, 用于查找 grain运行时间类型。</param>
        /// <returns>指指定的 grain。</returns>
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey;

        /// <summary>
        /// 获取引用 grain。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">关键是 grain.</param>
        /// <param name="grainClassNamePrefix">一个可选类名前缀, 用于查找 grain运行时间类型。</param>
        /// <returns>指指定的 grain。</returns>
        TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithStringKey;

        /// <summary>
        /// 获取引用 grain。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">关键是 grain.</param>
        /// <param name="keyExtension">关键扩展 grain.</param>
        /// <param name="grainClassNamePrefix">一个可选类名前缀, 用于查找 grain运行时间类型。</param>
        /// <returns>指指定的 grain。</returns>
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidCompoundKey;

        /// <summary>
        /// 获取引用 grain。
        /// </summary>
        /// <typeparam name="TGrainInterface">接口类型。</typeparam>
        /// <param name="primaryKey">关键是 grain.</param>
        /// <param name="keyExtension">关键扩展 grain.</param>
        /// <param name="grainClassNamePrefix">一个可选类名前缀, 用于查找 grain运行时间类型。</param>
        /// <returns>指指定的 grain。</returns>
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerCompoundKey;

        /// <summary>
        /// 创建所提供 <paramref name="obj"/>的引用。
        /// </summary>
        /// <typeparam name="TGrainObserverInterface">
        /// 具体 <see cref="IGrainObserver"/> 类型 <paramref name="obj"/>.
        /// </typeparam>
        /// <param name="obj">创建引用的对象。</param>
        /// <returns>提及 <paramref name="obj"/>.</returns>
        TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver;

        /// <summary>
        /// 删除所提供的对象参考。
        /// </summary>
        /// <typeparam name="TGrainObserverInterface">
        /// 具体 <see cref="IGrainObserver"/> 类型 <paramref name="obj"/>.
        /// </typeparam>
        /// <param name="obj">引用正在删除 。</param>
        /// <returns>A <see cref="Task"/> 代表所完成的工作。</returns>
        void DeleteObjectReference<TGrainObserverInterface>(IGrainObserver obj) where TGrainObserverInterface : IGrainObserver;

        /// <summary>
        /// 返回所提供接口类型的主要实施且有主要密钥的 grain 。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 下 grain 返回的界面类型 grain 参考必须执行。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// grain主键
        /// </param>
        /// <returns>
        /// 提及 grain ,这是所提供接口类型的主要实施,并提供了主钥匙。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey);

        /// <summary>
        /// 返回所提供接口类型的主要实施且有主要密钥的 grain 。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 下 grain 返回的界面类型 grain 参考必须执行。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// grain主键
        /// </param>
        /// <returns>
        /// 提及 grain ,这是所提供接口类型的主要实施,并提供了主钥匙。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey);

        /// <summary>
        /// 返回所提供接口类型的主要实施且有主要密钥的 grain 。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 下 grain 返回的界面类型 grain 参考必须执行。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// grain主键
        /// </param>
        /// <returns>
        /// 提及 grain ,这是所提供接口类型的主要实施,并提供了主钥匙。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey);

        /// <summary>
        /// 返回所提供接口类型的主要实施且有主要密钥的 grain 。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 下 grain 返回的界面类型 grain 参考必须执行。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// grain主键
        /// </param>
        /// <param name="keyExtension">
        /// grain 关键扩展组件。
        /// </param>
        /// <returns>
        /// 提及 grain ,这是所提供接口类型的主要实施,并提供了主钥匙。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey, string keyExtension);

        /// <summary>
        /// 返回所提供接口类型的主要实施且有主要密钥的 grain 。
        /// </summary>
        /// <param name="grainInterfaceType">
        /// 下 grain 返回的界面类型 grain 参考必须执行。
        /// </param>
        /// <param name="grainPrimaryKey">
        /// grain主键
        /// </param>
        /// <param name="keyExtension">
        /// grain 关键扩展组件。
        /// </param>
        /// <returns>
        /// 提及 grain ,这是所提供接口类型的主要实施,并提供了主钥匙。
        /// </returns>
        IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey, string keyExtension);

        /// <summary>
        /// 返回指定执行指定接口的 grain 引用。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <typeparam name="TGrainInterface">
        /// 下 grain 返回的界面类型 grain 参考必须执行。
        /// </typeparam>
        /// <returns>
        /// 引用执行指定接口的指定 grain 。
        /// </returns>
        TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable;

        /// <summary>
        /// 返回为提供 grain id 提供的未打字引用。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <returns>
        /// 提供的 grain id 的未类型引用。
        /// </returns>
        IAddressable GetGrain(GrainId grainId);

        /// <summary>
        /// 返回提供执行指定接口类型的 grain id的引用。
        /// </summary>
        /// <param name="grainId">
        /// 下 grain id. 数据。
        /// </param>
        /// <param name="interfaceType">
        /// 返回的 grain 引用必须执行的接口类型。
        /// </param>
        /// <returns>
        /// 用于提供执行指定接口类型的 grain id的参考文献。
        /// </returns>
        IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType);

        /// <summary>
        /// 返回提供执行指定接口类型的 grain id的引用。
        /// </summary>
        /// <param name="interfaceType">下 grain 返回的界面类型 grain 参考必须执行。</param>
        /// <param name="grainKey">关键是 grain</param>
        /// <param name="grainClassNamePrefix">类名前缀用于查找 grain运行时间类型。</param>
        /// <returns>
        /// 用于提供执行指定接口类型的 grain id的参考文献。
        /// </returns>
        IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string grainClassNamePrefix);

        /// <summary>
        /// 返回提供执行指定接口类型的 grain id的引用。
        /// </summary>
        /// <param name="interfaceType">下 grain 返回的界面类型 grain 参考必须执行。</param>
        /// <param name="grainKey">关键是 grain</param>
        /// <returns>
        /// 用于提供执行指定接口类型的 grain id的参考文献。
        /// </returns>
        IAddressable GetGrain(Type interfaceType, IdSpan grainKey);
    }
}
