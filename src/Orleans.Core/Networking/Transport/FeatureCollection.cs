using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.Connections.Transport;

/// <summary>
/// <see cref="IFeatureCollection"/> 的默认实现。
/// </summary>
public class FeatureCollection : IFeatureCollection
{
    private static readonly KeyComparer FeatureKeyComparer = new KeyComparer();
    private readonly IFeatureCollection? _defaults;
    private readonly int _initialCapacity;
    private IDictionary<Type, object>? _features;
    private volatile int _containerRevision;

    /// <summary>
    /// 初始化 <see cref="FeatureCollection"/> 的新实例。
    /// </summary>
    public FeatureCollection()
    {
    }

    /// <summary>
    /// 使用指定初始容量初始化 <see cref="FeatureCollection"/> 的新实例。
    /// </summary>
    /// <param name="initialCapacity">集合最初能够包含的元素数量。</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="initialCapacity"/> 小于 0。</exception>
    public FeatureCollection(int initialCapacity)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        _initialCapacity = initialCapacity;
    }

    /// <summary>
    /// 使用指定默认值初始化 <see cref="FeatureCollection"/> 的新实例。
    /// </summary>
    /// <param name="defaults">功能的默认值。</param>
    public FeatureCollection(IFeatureCollection defaults)
    {
        _defaults = defaults;
    }

    /// <inheritdoc />
    public virtual int Revision => _containerRevision + (_defaults?.Revision ?? 0);

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public object? this[Type key]
    {
        get
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            return _features != null && _features.TryGetValue(key, out var result) ? result : _defaults?[key];
        }

        set
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (value == null)
            {
                if (_features != null && _features.Remove(key))
                {
                    _containerRevision++;
                }
                return;
            }

            _features ??= new Dictionary<Type, object>(_initialCapacity);
            _features[key] = value;
            _containerRevision++;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        if (_features != null)
        {
            foreach (var pair in _features)
            {
                yield return pair;
            }
        }

        if (_defaults != null)
        {
            // 不返回被包装器屏蔽的功能。
            foreach (var pair in _features == null ? _defaults : _defaults.Except(_features, FeatureKeyComparer))
            {
                yield return pair;
            }
        }
    }

    /// <inheritdoc />
    public TFeature? Get<TFeature>() => (TFeature?)this[typeof(TFeature)];

    /// <inheritdoc />
    public void Set<TFeature>(TFeature? instance) => this[typeof(TFeature)] = instance;

    private sealed class KeyComparer : IEqualityComparer<KeyValuePair<Type, object>>
    {
        public bool Equals(KeyValuePair<Type, object> x, KeyValuePair<Type, object> y) => x.Key.Equals(y.Key);

        public int GetHashCode(KeyValuePair<Type, object> obj) => obj.Key.GetHashCode();
    }
}
