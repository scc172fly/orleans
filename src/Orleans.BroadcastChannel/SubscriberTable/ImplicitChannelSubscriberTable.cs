using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.BroadcastChannel.SubscriberTable
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "AddBroadcastChannel registers this type as a singleton constructed by dependency injection.")]
    internal class ImplicitChannelSubscriberTable
    {
        private readonly Lock _lockObj = new();
        private readonly GrainBindingsResolver _bindings;
        private readonly IChannelNamespacePredicateProvider[] _providers;
        private readonly IServiceProvider _serviceProvider;
        private Cache _cache;

        public ImplicitChannelSubscriberTable(
            GrainBindingsResolver bindings,
            IEnumerable<IChannelNamespacePredicateProvider> providers,
            IServiceProvider serviceProvider)
        {
            _bindings = bindings;
            var initialBindings = bindings.GetAllBindings();
            _providers = providers.ToArray();
            _serviceProvider = serviceProvider;
            _cache = BuildCache(initialBindings.Version, initialBindings.Bindings);
        }

        private Cache GetCache()
        {
            var cache = _cache;
            var bindings = _bindings.GetAllBindings();
            if (bindings.Version == cache.Version)
            {
                return cache;
            }

            lock (_lockObj)
            {
                bindings = _bindings.GetAllBindings();
                if (bindings.Version == cache.Version)
                {
                    return cache;
                }

                return _cache = BuildCache(bindings.Version, bindings.Bindings);
            }
        }

        private Cache BuildCache(MajorMinorVersion version, ImmutableDictionary<GrainType, GrainBindings> bindings)
        {
            var newPredicates = new List<BroadcastChannelSubscriberPredicate>();

            foreach (var binding in bindings.Values)
            {
                foreach (var grainBinding in binding.Bindings)
                {
                    if (!grainBinding.TryGetValue(WellKnownGrainTypeProperties.BindingTypeKey, out var type)
                        || type != WellKnownGrainTypeProperties.BroadcastChannelBindingTypeValue)
                    {
                        continue;
                    }

                    if (!grainBinding.TryGetValue(WellKnownGrainTypeProperties.BroadcastChannelBindingPatternKey, out var pattern))
                    {
                        throw new KeyNotFoundException(
                           $"Channel binding for grain type {binding.GrainType} is missing a \"{WellKnownGrainTypeProperties.BroadcastChannelBindingPatternKey}\" value");
                    }

                    IChannelNamespacePredicate? predicate = null;
                    foreach (var provider in _providers)
                    {
                        if (provider.TryGetPredicate(pattern, out predicate)) break;
                    }

                    if (predicate is null)
                    {
                        throw new KeyNotFoundException(
                            $"Could not find an {nameof(IChannelNamespacePredicate)} for the pattern \"{pattern}\"."
                            + $" Ensure that a corresponding {nameof(IChannelNamespacePredicateProvider)} is registered");
                    }

                    if (!grainBinding.TryGetValue(WellKnownGrainTypeProperties.ChannelIdMapperKey, out var mapperName))
                    {
                        throw new KeyNotFoundException(
                           $"Channel binding for grain type {binding.GrainType} is missing a \"{WellKnownGrainTypeProperties.ChannelIdMapperKey}\" value");
                    }

                    var channelIdMapper = _serviceProvider.GetKeyedService<IChannelIdMapper>(string.IsNullOrWhiteSpace(mapperName) ? DefaultChannelIdMapper.Name : mapperName);
                    var subscriber = new BroadcastChannelSubscriber(binding, channelIdMapper!);
                    newPredicates.Add(new BroadcastChannelSubscriberPredicate(subscriber, predicate));
                }
            }

            return new Cache(version, newPredicates);
        }

        /// <summary>
        /// 根据通道 ID 获取从隐式订阅 ID 到隐式订阅者的映射。如果通道 ID 没有关联的命名空间，此方法将抛出异常。
        /// </summary>
        /// <param name="channelId">通道 ID。</param>
        /// <param name="grainFactory">用于获取消费者引用的 Grain 工厂。</param>
        /// <returns>隐式订阅的 Grain 引用集合。这些 Grain 应支持广播通道消费者扩展。</returns>
        /// <exception cref="ArgumentException">通道 ID 没有关联的命名空间。</exception>
        /// <exception cref="InvalidOperationException">违反内部不变量。</exception>
        internal Dictionary<Guid, IBroadcastChannelConsumerExtension> GetImplicitSubscribers(InternalChannelId channelId, IGrainFactory grainFactory)
        {
            var channelNamespace = channelId.GetNamespace();
            if (string.IsNullOrWhiteSpace(channelNamespace))
            {
                throw new ArgumentException("The channel ID doesn't have an associated namespace.", nameof(channelId));
            }

            var entries = GetOrAddImplicitSubscribers(channelNamespace);

            var result = new Dictionary<Guid, IBroadcastChannelConsumerExtension>();
            foreach (var entry in entries)
            {
                var consumer = MakeConsumerReference(grainFactory, channelId, entry);
                var subscriptionGuid = MakeSubscriptionGuid(entry.GrainType, channelId);
                CollectionsMarshal.GetValueRefOrAddDefault(result, subscriptionGuid, out var duplicate) = consumer;
                if (duplicate)
                {
                    throw new InvalidOperationException(
                        $"Internal invariant violation: generated duplicate subscriber reference: {consumer}, subscriptionId: {subscriptionGuid}");
                }
            }
            return result;
        }

        private HashSet<BroadcastChannelSubscriber> GetOrAddImplicitSubscribers(string channelNamespace)
        {
            var cache = GetCache();
            if (cache.Namespaces.TryGetValue(channelNamespace, out var result))
            {
                return result;
            }

            return cache.Namespaces.GetOrAdd(channelNamespace, FindImplicitSubscribers(channelNamespace, cache.Predicates));
        }

        /// <summary>
        /// 为 grainId、grainType 和命名空间的每种组合创建唯一的 subscriptionId。
        /// </summary>
        private Guid MakeSubscriptionGuid(GrainType grainType, InternalChannelId channelId)
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, grainType.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], channelId.ChannelId.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], channelId.ChannelId.GetKeyIndex());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], StableHash.ComputeHash(channelId.ProviderName));
            bytes[15] |= 0x80; // 设置最后一个字节的最高位（表示隐式订阅）。
            return new(bytes);
        }

        /// <summary>
        /// 查找给定通道命名空间的所有隐式订阅者。
        /// </summary>
        private static HashSet<BroadcastChannelSubscriber> FindImplicitSubscribers(string channelNamespace, List<BroadcastChannelSubscriberPredicate> predicates)
        {
            var result = new HashSet<BroadcastChannelSubscriber>();
            foreach (var predicate in predicates)
            {
                if (predicate.Predicate.IsMatch(channelNamespace))
                {
                    result.Add(predicate.Subscriber);
                }
            }

            return result;
        }

        /// <summary>
        /// 创建应支持广播通道消费者扩展的 Grain 的引用。
        /// </summary>
        /// <param name="grainFactory">用于获取消费者引用的 Grain 工厂。</param>
        /// <param name="channelId">用于构造 Grain ID 的通道 ID。</param>
        /// <param name="channelSubscriber">要创建的 Grain 的 GrainBindings。</param>
        /// <returns></returns>
        private static IBroadcastChannelConsumerExtension MakeConsumerReference(
            IGrainFactory grainFactory,
            InternalChannelId channelId,
            BroadcastChannelSubscriber channelSubscriber)
        {
            var grainId = channelSubscriber.GetGrainId(channelId);
            return grainFactory.GetGrain<IBroadcastChannelConsumerExtension>(grainId);
        }

        private class BroadcastChannelSubscriberPredicate
        {
            public BroadcastChannelSubscriberPredicate(BroadcastChannelSubscriber subscriber, IChannelNamespacePredicate predicate)
            {
                Subscriber = subscriber;
                Predicate = predicate;
            }

            public BroadcastChannelSubscriber Subscriber { get; }
            public IChannelNamespacePredicate Predicate { get; }
        }

        private sealed class BroadcastChannelSubscriber : IEquatable<BroadcastChannelSubscriber?>
        {
            public BroadcastChannelSubscriber(GrainBindings grainBindings, IChannelIdMapper channelIdMapper)
            {
                GrainBindings = grainBindings;
                this.channelIdMapper = channelIdMapper;
            }

            public GrainType GrainType => GrainBindings.GrainType;

            private GrainBindings GrainBindings { get; }

            private IChannelIdMapper channelIdMapper { get; }

            public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as BroadcastChannelSubscriber);

            public bool Equals([NotNullWhen(true)] BroadcastChannelSubscriber? other) => other != null && GrainType.Equals(other.GrainType);

            public override int GetHashCode() => GrainType.GetHashCode();

            internal GrainId GetGrainId(InternalChannelId channelId)
            {
                var grainKeyId = channelIdMapper.GetGrainKeyId(GrainBindings, channelId);
                return GrainId.Create(GrainType, grainKeyId);
            }
        }

        private class Cache
        {
            public Cache(MajorMinorVersion version, List<BroadcastChannelSubscriberPredicate> predicates)
            {
                Version = version;
                Predicates = predicates;
                Namespaces = new ConcurrentDictionary<string, HashSet<BroadcastChannelSubscriber>>();
            }

            public MajorMinorVersion Version { get; }
            public ConcurrentDictionary<string, HashSet<BroadcastChannelSubscriber>> Namespaces { get; }
            public List<BroadcastChannelSubscriberPredicate> Predicates { get; }
        }
    }
}
