using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Orleans.Connections.Transport;

/// <summary>
/// <see cref="IFeatureCollection"/>的默认执行。
/// </summary>
public class FeatureCollection : IFeatureCollection
{
    private static readonly KeyComparer FeatureKeyComparer = new KeyComparer();
    private readonly IFeatureCollection? _defaults;
    private readonly int _initialCapacity;
    private IDictionary<Type, object>? _features;
    private volatile int _containerRevision;

    /// <summary>
    /// 初始化 <see cref="FeatureCollection"/>新实例。
    /// </summary>
    public FeatureCollection()
    {
    }

    /// <summary>
    /// 启动一个 <see cref="FeatureCollection"/> 新实例,并具备指定的初始能力。
    /// </summary>
    /// <param name="initialCapacity">收藏中可包含元素的初始数。</param>
    /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="initialCapacity"/> 小于0</exception>
    public FeatureCollection(int initialCapacity)
    {
        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        _initialCapacity = initialCapacity;
    }

    /// <summary>
    /// 初始化指定默认值的 <see cref="FeatureCollection"/> 新实例。
    /// </summary>
    /// <param name="defaults">特性默认值。</param>
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
            // 不要把包装纸遮住的特征还回去
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
