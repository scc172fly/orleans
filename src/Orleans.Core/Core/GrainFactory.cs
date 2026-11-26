using System;
using System.Collections.Generic;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 进入 grains工厂。
    /// </summary>
    internal class GrainFactory : IInternalGrainFactory
    {
        private GrainReferenceRuntime? grainReferenceRuntime;

        /// <summary>
        /// 输入系统目标参考文件的缓存 。
        /// </summary>
        private readonly Dictionary<(GrainId, Type), ISystemTarget> typedSystemTargetReferenceCache = new Dictionary<(GrainId, Type), ISystemTarget>();

        private readonly GrainReferenceActivator referenceActivator;
        private readonly GrainInterfaceTypeResolver interfaceTypeResolver;
        private readonly GrainInterfaceTypeToGrainTypeResolver interfaceTypeToGrainTypeResolver;
        private readonly IRuntimeClient runtimeClient;

        public GrainFactory(
            IRuntimeClient runtimeClient,
            GrainReferenceActivator referenceActivator,
            GrainInterfaceTypeResolver interfaceTypeResolver,
            GrainInterfaceTypeToGrainTypeResolver interfaceToTypeResolver)
        {
            this.runtimeClient = runtimeClient;
            this.referenceActivator = referenceActivator;
            this.interfaceTypeResolver = interfaceTypeResolver;
            this.interfaceTypeToGrainTypeResolver = interfaceToTypeResolver;
        }

        private GrainReferenceRuntime GrainReferenceRuntime => this.grainReferenceRuntime ??= (GrainReferenceRuntime)this.runtimeClient.GrainReferenceRuntime;

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithGuidKey
        {
            var grainKey = GrainIdKeyExtensions.CreateGuidKey(primaryKey);
            return (TGrainInterface)GetGrain(typeof(TGrainInterface), grainKey, grainClassNamePrefix: grainClassNamePrefix);
        }

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) where TGrainInterface : IGrainWithIntegerKey
        {
            var grainKey = GrainIdKeyExtensions.CreateIntegerKey(primaryKey);
            return (TGrainInterface)GetGrain(typeof(TGrainInterface), grainKey, grainClassNamePrefix: grainClassNamePrefix);
        }

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithStringKey
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(primaryKey);
            var grainKey = IdSpan.Create(primaryKey);
            return (TGrainInterface)GetGrain(typeof(TGrainInterface), grainKey, grainClassNamePrefix: grainClassNamePrefix);
        }

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithGuidCompoundKey
        {
            ValidateGrainKeyExtension(keyExtension);

            var grainKey = GrainIdKeyExtensions.CreateGuidKey(primaryKey, keyExtension);
            return (TGrainInterface)GetGrain(typeof(TGrainInterface), grainKey, grainClassNamePrefix: grainClassNamePrefix);
        }

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null)
            where TGrainInterface : IGrainWithIntegerCompoundKey
        {
            ValidateGrainKeyExtension(keyExtension);

            var grainKey = GrainIdKeyExtensions.CreateIntegerKey(primaryKey, keyExtension);
            return (TGrainInterface)GetGrain(typeof(TGrainInterface), grainKey, grainClassNamePrefix: grainClassNamePrefix);
        }


        /// <inheritdoc />
        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IGrainObserver obj)
            where TGrainObserverInterface : IGrainObserver
        {
            return this.CreateObjectReference<TGrainObserverInterface>((IAddressable)obj);
        }

        /// <inheritdoc />
        public void DeleteObjectReference<TGrainObserverInterface>(
            IGrainObserver obj) where TGrainObserverInterface : IGrainObserver
        {
            this.runtimeClient.DeleteObjectReference(obj);
        }

        /// <inheritdoc />
        public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(IAddressable obj)
                where TGrainObserverInterface : IAddressable
        {
            return (TGrainObserverInterface)this.CreateObjectReference(typeof(TGrainObserverInterface), obj);
        }

        /// <inheritdoc />
        public TGrainInterface Cast<TGrainInterface>(IAddressable grain)
        {
            var interfaceType = typeof(TGrainInterface);
            return (TGrainInterface)this.Cast(grain, interfaceType);
        }

        /// <inheritdoc />
        public object Cast(IAddressable grain, Type interfaceType) => this.GrainReferenceRuntime.Cast(grain, interfaceType);

        public TGrainInterface GetSystemTarget<TGrainInterface>(GrainType grainType, SiloAddress destination)
            where TGrainInterface : ISystemTarget
        {
            var grainId = SystemTargetGrainId.Create(grainType, destination);
            return this.GetSystemTarget<TGrainInterface>(grainId.GrainId);
        }

        /// <inheritdoc />
        public TGrainInterface GetSystemTarget<TGrainInterface>(GrainId grainId)
            where TGrainInterface : ISystemTarget
        {
            ISystemTarget? reference;
            ValueTuple<GrainId, Type> key = ValueTuple.Create(grainId, typeof(TGrainInterface));

            lock (this.typedSystemTargetReferenceCache)
            {
                if (this.typedSystemTargetReferenceCache.TryGetValue(key, out reference))
                {
                    return (TGrainInterface)reference;
                }

                reference = this.GetGrain<TGrainInterface>(grainId);
                this.typedSystemTargetReferenceCache[key] = reference;
                return (TGrainInterface)reference;
            }
        }

        /// <inheritdoc />
        public TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) where TGrainInterface : IAddressable
        {
            return (TGrainInterface)this.CreateGrainReference(typeof(TGrainInterface), grainId);
        }

        /// <inheritdoc />
        public IAddressable GetGrain(GrainId grainId) => this.referenceActivator.CreateReference(grainId, default);

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, Guid key)
        {
            var grainKey = GrainIdKeyExtensions.CreateGuidKey(key);
            return (IGrain)GetGrain(grainInterfaceType, grainKey, grainClassNamePrefix: null);
        }

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, long key)
        {
            var grainKey = GrainIdKeyExtensions.CreateIntegerKey(key);
            return (IGrain)GetGrain(grainInterfaceType, grainKey, grainClassNamePrefix: null);
        }

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, string key)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
            var grainKey = IdSpan.Create(key);
            return (IGrain)GetGrain(grainInterfaceType, grainKey, grainClassNamePrefix: null);
        }

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, Guid key, string keyExtension)
        {
            var grainKey = GrainIdKeyExtensions.CreateGuidKey(key, keyExtension);
            return (IGrain)GetGrain(grainInterfaceType, grainKey, grainClassNamePrefix: null);
        }

        /// <inheritdoc />
        public IGrain GetGrain(Type grainInterfaceType, long key, string keyExtension)
        {
            var grainKey = GrainIdKeyExtensions.CreateIntegerKey(key, keyExtension);
            return (IGrain)GetGrain(grainInterfaceType, grainKey, grainClassNamePrefix: null);
        }

        /// <inheritdoc />
        public IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType)
        {
            return this.referenceActivator.CreateReference(grainId, interfaceType);
        }

        /// <inheritdoc />
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey) => GetGrain(interfaceType, grainKey, null);

        /// <summary>
        /// 获取一个 grain 引用,该引用可执行指定的 grain 接口类型并具有指定的 grain 密钥,而没有直接指定 grain 类型。
        /// </summary>
        /// <remarks>
        /// 这种方法推断出最适当的方法 <see cref="GrainId.Type"/> 以 <paramref name="interfaceType"/> 和可选 <paramref name="grainClassNamePrefix"/> 参数。
        /// <see cref="GrainInterfaceTypeToGrainTypeResolver"/> 类型负责确定最适当的 grain 类型。
        /// </remarks>
        /// <param name="interfaceType">返回的界面类型 grain 将执行参考文献。</param>
        /// <param name="grainKey">下 <see cref="GrainId.Key"/> 部分的 grain id. 数据。</param>
        /// <param name="grainClassNamePrefix">类名前缀的可选 grain 类名</param>
        /// <returns>A grain 执行所提供接口的引用。</returns>
        public IAddressable GetGrain(Type interfaceType, IdSpan grainKey, string? grainClassNamePrefix = null)
        {
            ArgumentNullException.ThrowIfNull(interfaceType);

            var grainInterfaceType = this.interfaceTypeResolver.GetGrainInterfaceType(interfaceType);

            GrainType grainType;
            if (!string.IsNullOrWhiteSpace(grainClassNamePrefix))
            {
                grainType = this.interfaceTypeToGrainTypeResolver.GetGrainType(grainInterfaceType, grainClassNamePrefix);
            }
            else
            {
                grainType = this.interfaceTypeToGrainTypeResolver.GetGrainType(grainInterfaceType);
            }

            var grainId = GrainId.Create(grainType, grainKey);
            var grain = this.referenceActivator.CreateReference(grainId, grainInterfaceType);
            return grain;
        }

        /// <summary>
        /// 创建 grain 引用。
        /// </summary>
        /// <param name="interfaceType">引用必须执行的接口类型。</param>
        /// <param name="grainId">下 grain 参考目标。</param>
        /// <returns>A grain 参考。</returns>
        private object CreateGrainReference(Type interfaceType, GrainId grainId)
        {
            var grainInterfaceType = this.interfaceTypeResolver.GetGrainInterfaceType(interfaceType);
            return this.referenceActivator.CreateReference(grainId, grainInterfaceType);
        }

        /// <summary>
        /// 创建对象引用,指向所提供的对象。
        /// </summary>
        /// <param name="interfaceType">引用必须执行的接口类型。</param>
        /// <param name="obj">可处理对象执行。</param>
        /// <returns>对象引用。</returns>
        private object CreateObjectReference(Type interfaceType, IAddressable obj)
        {
            if (!interfaceType.IsInterface)
            {
                throw new ArgumentException(
                    $"The provided type parameter must be an interface. '{interfaceType.FullName}' is not an interface.");
            }

            if (!interfaceType.IsInstanceOfType(obj))
            {
                throw new ArgumentException($"The provided object must implement '{interfaceType.FullName}'.", nameof(obj));
            }

            return this.Cast(this.runtimeClient.CreateObjectReference(obj), interfaceType);
        }

        /// <summary>
        /// 校验所提供的 grain 密钥扩展名。
        /// </summary>
        /// <param name="keyExt"> grain 密钥扩展名。</param>
        /// <exception cref="ArgumentNullException">密钥是 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentException">密钥为空或仅包含白空间。</exception>
        private static void ValidateGrainKeyExtension(string keyExt)
        {
            if (!string.IsNullOrWhiteSpace(keyExt)) return;

            if (null == keyExt)
            {
                throw new ArgumentNullException(nameof(keyExt));
            }

            throw new ArgumentException("Key extension is empty or white space.", nameof(keyExt));
        }
    }
}
