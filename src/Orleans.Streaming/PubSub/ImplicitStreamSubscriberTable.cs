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

namespace Orleans.Streams
{
    internal class ImplicitStreamSubscriberTable
    {
        private readonly Lock _lockObj = new();
        private readonly GrainBindingsResolver _bindings;
        private readonly IStreamNamespacePredicateProvider[] _providers;
        private readonly IServiceProvider _serviceProvider;
        private Cache _cache;

        public ImplicitStreamSubscriberTable(
            GrainBindingsResolver bindings,
            IEnumerable<IStreamNamespacePredicateProvider> providers,
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
            var newPredicates = new List<StreamSubscriberPredicate>();

            foreach (var binding in bindings.Values)
            {
                foreach (var grainBinding in binding.Bindings)
                {
                    if (!grainBinding.TryGetValue(WellKnownGrainTypeProperties.BindingTypeKey, out var type)
                        || type != WellKnownGrainTypeProperties.StreamBindingTypeValue)
                    {
                        continue;
                    }

                    if (!grainBinding.TryGetValue(WellKnownGrainTypeProperties.StreamBindingPatternKey, out var pattern))
                    {
                        throw new KeyNotFoundException(
                           $"Stream binding for grain type {binding.GrainType} is missing a \"{WellKnownGrainTypeProperties.StreamBindingPatternKey}\" value");
                    }

                    IStreamNamespacePredicate? predicate = null;
                    foreach (var provider in _providers)
                    {
                        if (provider.TryGetPredicate(pattern, out predicate)) break;
                    }

                    if (predicate is null)
                    {
                        throw new KeyNotFoundException(
                            $"Could not find an {nameof(IStreamNamespacePredicate)} for the pattern \"{pattern}\"."
                            + $" Ensure that a corresponding {nameof(IStreamNamespacePredicateProvider)} is registered");
                    }

                    if (!grainBinding.TryGetValue(WellKnownGrainTypeProperties.StreamIdMapperKey, out var mapperName))
                    {
                        throw new KeyNotFoundException(
                           $"Stream binding for grain type {binding.GrainType} is missing a \"{WellKnownGrainTypeProperties.StreamIdMapperKey}\" value");
                    }

                    var streamIdMapper = _serviceProvider.GetKeyedService<IStreamIdMapper>(string.IsNullOrWhiteSpace(mapperName) ? DefaultStreamIdMapper.Name : mapperName)!; // 地图仪总是在解密键下注册。
                    var subscriber = new StreamSubscriber(binding, streamIdMapper);
                    newPredicates.Add(new StreamSubscriberPredicate(subscriber, predicate));
                }
            }

            return new Cache(version, newPredicates);
        }

        /// <summary>
        /// 获取指定流的暗含订阅者 grain ID的默认订阅标识图。
        /// </summary>
        /// <param name="streamId">流号。</param>
        /// <returns>用于暗含订阅的字典绘图订阅ID grain ID. 身份证 grains 预计这将支持消费者的扩展。</returns>
        /// <exception cref="System.ArgumentException">流代号没有相关的命名空间。</exception>
        /// <exception cref="System.InvalidOperationException">内部的无变不化侵犯。</exception>
        internal Dictionary<Guid, GrainId> GetImplicitSubscribers(QualifiedStreamId streamId)
        {
            var streamNamespace = streamId.GetNamespace();
            if (!IsImplicitSubscribeEligibleNameSpace(streamNamespace))
            {
                throw new ArgumentException("The stream ID doesn't have an associated namespace.", nameof(streamId));
            }

            var entries = GetOrAddImplicitSubscribers(streamNamespace);

            var result = new Dictionary<Guid, GrainId>();
            foreach (var entry in entries)
            {
                var grainId = entry.GetGrainId(streamId);
                Guid subscriptionGuid = MakeSubscriptionGuid(entry.GrainType, streamId);
                CollectionsMarshal.GetValueRefOrAddDefault(result, subscriptionGuid, out var duplicate) = grainId;
                if (duplicate)
                {
                    throw new InvalidOperationException(
                        $"Internal invariant violation: generated duplicate subscriber reference: {grainId}, subscriptionId: {subscriptionGuid}");
                }
            }
            return result;
        }

        private HashSet<StreamSubscriber> GetOrAddImplicitSubscribers(string streamNamespace)
        {
            var cache = GetCache();
            if (cache.Namespaces.TryGetValue(streamNamespace, out var result))
            {
                return result;
            }

            return cache.Namespaces.GetOrAdd(streamNamespace, FindImplicitSubscribers(streamNamespace, cache.Predicates));
        }

        /// <summary>
        /// 确定指定的 grain 是否为特定流的默认用户。
        /// </summary>
        /// <param name="grainId"> grain 标识符。</param>
        /// <param name="streamId">流体识别符号。</param>
        /// <returns>如果真实 grain ID 表示流代号所描述的溪流的暗含用户。</returns>
        internal bool IsImplicitSubscriber(GrainId grainId, QualifiedStreamId streamId)
        {
            var streamNamespace = streamId.GetNamespace();
            if (!IsImplicitSubscribeEligibleNameSpace(streamNamespace))
            {
                return false;
            }

            foreach (var entry in GetOrAddImplicitSubscribers(streamNamespace))
            {
                if (entry.GrainType == grainId.Type)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 尝试获取隐含 subscriptionId。
        /// 如果存在默认订阅,则返回每个 grain 类型、 grainId名称空间组合独有的订阅编号。
        /// </summary>
        /// <param name="grainId"></param>
        /// <param name="streamId"></param>
        /// <param name="subscriptionId"></param>
        /// <returns></returns>
        internal bool TryGetImplicitSubscriptionGuid(GrainId grainId, QualifiedStreamId streamId, out Guid subscriptionId)
        {
            if (!IsImplicitSubscriber(grainId, streamId))
            {
                subscriptionId = default;
                return false;
            }

            subscriptionId = MakeSubscriptionGuid(grainId.Type, streamId);
            return true;
        }

        /// <summary>
        /// 创建 subscriptionId 唯一 grainId, grainType,命名空间组合。
        /// </summary>
        private Guid MakeSubscriptionGuid(GrainType grainType, QualifiedStreamId streamId)
        {
            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, grainType.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], streamId.StreamId.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], streamId.StreamId.GetKeyIndex());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], StableHash.ComputeHash(streamId.ProviderName));
            return SubscriptionMarker.MarkAsImplictSubscriptionId(new(bytes));
        }

        internal static bool IsImplicitSubscribeEligibleNameSpace([NotNullWhen(true)] string? streamNameSpace)
        {
            return !string.IsNullOrWhiteSpace(streamNameSpace);
        }

        /// <summary>
        /// 查找给定流命名空间的所有隐含用户。
        /// </summary>
        private static HashSet<StreamSubscriber> FindImplicitSubscribers(string streamNamespace, List<StreamSubscriberPredicate> predicates)
        {
            var result = new HashSet<StreamSubscriber>();
            foreach (var predicate in predicates)
            {
                if (predicate.Predicate.IsMatch(streamNamespace))
                {
                    result.Add(predicate.Subscriber);
                }
            }

            return result;
        }

        private class StreamSubscriberPredicate
        {
            public StreamSubscriberPredicate(StreamSubscriber subscriber, IStreamNamespacePredicate predicate)
            {
                this.Subscriber = subscriber;
                this.Predicate = predicate;
            }

            public StreamSubscriber Subscriber { get; }
            public IStreamNamespacePredicate Predicate { get; }
        }

        private sealed class StreamSubscriber : IEquatable<StreamSubscriber?>
        {
            public StreamSubscriber(GrainBindings grainBindings, IStreamIdMapper streamIdMapper)
            {
                this.GrainBindings = grainBindings;
                this.streamIdMapper = streamIdMapper;
            }

            public GrainType GrainType => this.GrainBindings.GrainType;

            private GrainBindings GrainBindings { get; }

            private IStreamIdMapper streamIdMapper { get; }

            public override bool Equals(object? obj) => Equals(obj as StreamSubscriber);

            public bool Equals(StreamSubscriber? other) => other is not null && GrainType.Equals(other.GrainType);

            public override int GetHashCode() => GrainType.GetHashCode();

            internal GrainId GetGrainId(QualifiedStreamId streamId)
            {
                var grainKeyId = this.streamIdMapper.GetGrainKeyId(this.GrainBindings, streamId);
                return GrainId.Create(this.GrainType, grainKeyId);
            }
        }

        private class Cache
        {
            public Cache(MajorMinorVersion version, List<StreamSubscriberPredicate> predicates)
            {
                this.Version = version;
                this.Predicates = predicates;
                this.Namespaces = new ConcurrentDictionary<string, HashSet<StreamSubscriber>>();
            }

            public MajorMinorVersion Version { get; }
            public ConcurrentDictionary<string, HashSet<StreamSubscriber>> Namespaces { get; }
            public List<StreamSubscriberPredicate> Predicates { get; }
        }
    }
}
