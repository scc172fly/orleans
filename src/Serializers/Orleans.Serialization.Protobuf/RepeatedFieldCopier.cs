using System;
using System.Diagnostics.CodeAnalysis;
using Google.Protobuf.Collections;
using Orleans.Serialization.Cloning;

namespace Orleans.Serialization;

/// <summary>
/// <see cref="RepeatedField{T}"/>的复印机。
/// </summary>
/// <typeparam name="T">元素类型。</typeparam>
[RegisterCopier]
public sealed class RepeatedFieldCopier<T> : IDeepCopier<RepeatedField<T>>, IBaseCopier<RepeatedField<T>>
{
    private readonly IDeepCopier<T> _copier;

    /// <summary>
    /// 初始化 <see cref="RepeatedFieldCopier{T}"/> 类的新实例。
    /// </summary>
    /// <param name="valueCopier">价值复印机。</param>
    public RepeatedFieldCopier(IDeepCopier<T> valueCopier)
    {
        _copier = valueCopier;
    }

    /// <inheritdoc/>
    [return: NotNullIfNotNull(nameof(input))]
    public RepeatedField<T>? DeepCopy(RepeatedField<T>? input, CopyContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        if (input is null)
        {
            return null;
        }

        if (context.TryGetCopy<RepeatedField<T>>(input, out var result))
        {
            return result!;
        }

        if (input.GetType() != typeof(RepeatedField<T>))
        {
            return context.DeepCopy(input);
        }

        result = new RepeatedField<T> { Capacity = input.Count };
        context.RecordCopy(input, result);
        foreach (var item in input)
        {
            result.Add(_copier.DeepCopy(item, context)!);
        }

        return result;
    }

    /// <inheritdoc/>
    public void DeepCopy(RepeatedField<T> input, RepeatedField<T> output, CopyContext context)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));
        if (context is null) throw new ArgumentNullException(nameof(context));

        foreach (var item in input)
        {
            output.Add(_copier.DeepCopy(item, context)!);
        }
    }
}
