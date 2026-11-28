using System;
using System.Collections.Generic;
using Orleans.GrainReferences;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans
{
    /// <summary>
    /// 用于访问 Grain 的工厂。
    /// </summary>
    internal class GrainFactory : IInternalGrainFactory
    {
        private GrainReferenceRuntime? grainReferenceRuntime;

        /// <summary>
        /// 强类型系统目标引用的缓存。
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
        /// 获取实现指定 Grain 接口类型、具有指定 Grain 键的引用，而无需直接指定 Grain 类型。
        /// </summary>
        /// <remarks>
        /// 此方法推断最合适的 <see cref="GrainId.Type"/> 值，依据为 <paramref name="interfaceType"/> 参数以及可选的 <paramref name="grainClassNamePrefix"/> 参数。
        /// <see cref="GrainInterfaceTypeToGrainTypeResolver"/> 类型负责确定最合适的 Grain 类型。
        /// </remarks>
        /// <param name="interfaceType">返回的 Grain 引用将实现的接口类型。</param>
        /// <param name="grainKey">Grain ID 的 <see cref="GrainId.Key"/> 部分。</param>
        /// <param name="grainClassNamePrefix">可选的 Grain 类名前缀。</param>
        /// <returns>实现所提供接口的 Grain 引用。</returns>
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
        /// 创建 Grain 引用。
        /// </summary>
        /// <param name="interfaceType">该引用必须实现的接口类型。</param>
        /// <param name="grainId">该引用指向的 Grain ID。</param>
        /// <returns>Grain 引用。</returns>
        private object CreateGrainReference(Type interfaceType, GrainId grainId)
        {
            var grainInterfaceType = this.interfaceTypeResolver.GetGrainInterfaceType(interfaceType);
            return this.referenceActivator.CreateReference(grainId, grainInterfaceType);
        }

        /// <summary>
        /// 创建指向所提供对象的对象引用。
        /// </summary>
        /// <param name="interfaceType">该引用必须实现的接口类型。</param>
        /// <param name="obj">可寻址对象的实现。</param>
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
        /// 验证所提供的 Grain 扩展键。
        /// </summary>
        /// <param name="keyExt">Grain 扩展键。</param>
        /// <exception cref="ArgumentNullException">键为 <see langword="null"/>。</exception>
        /// <exception cref="ArgumentException">键为空或仅包含空白字符。</exception>
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
