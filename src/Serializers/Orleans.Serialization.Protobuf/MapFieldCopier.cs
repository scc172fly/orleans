using System;
using System.Diagnostics.CodeAnalysis;
using Google.Protobuf.Collections;
using Orleans.Serialization.Cloning;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="MapField{TKey, TValue}"/>的复印机。
/// </summary>
/// <typeparam name="TKey">t 密钥的类型。</typeparam>
/// <typeparam name="TValue">t 值的类型。</typeparam>
[RegisterCopier]
public sealed class MapFieldCopier<TKey, TValue> : IDeepCopier<MapField<TKey, TValue>>, IBaseCopier<MapField<TKey, TValue>>
{
    private readonly IDeepCopier<TKey> _keyCopier;
    private readonly IDeepCopier<TValue> _valueCopier;

    /// <summary>
    /// 初始化 <see cref="MapFieldCopier{TKey, TValue}"/> 类的新实例。
    /// </summary>
    /// <param name="keyCopier">钥匙复印机。</param>
    /// <param name="valueCopier">价值复印机。</param>
    public MapFieldCopier(IDeepCopier<TKey> keyCopier, IDeepCopier<TValue> valueCopier)
    {
        _keyCopier = keyCopier;
        _valueCopier = valueCopier;
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    public MapField<TKey, TValue>? DeepCopy(MapField<TKey, TValue>? input, CopyContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        if (input is null)
        {
            return null;
        }

        if (context.TryGetCopy<MapField<TKey, TValue>>(input, out var result))
        {
            return result!;
        }

        if (input.GetType() != typeof(MapField<TKey, TValue>))
        {
            return context.DeepCopy(input);
        }

        result = new MapField<TKey, TValue>();
        context.RecordCopy(input, result);
        foreach (var pair in input)
        {
            result[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
        }

        return result;
    }

    /// <inheritdoc/>
    public void DeepCopy(MapField<TKey, TValue> input, MapField<TKey, TValue> output, CopyContext context)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (context is null) throw new ArgumentNullException(nameof(context));

        foreach (var pair in input)
        {
            output[_keyCopier.DeepCopy(pair.Key, context)!] = _valueCopier.DeepCopy(pair.Value, context)!;
        }
    }
}
