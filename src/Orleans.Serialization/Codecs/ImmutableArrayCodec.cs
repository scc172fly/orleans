using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableArray{T}"/>的序列器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableArrayCodec<T> : GeneralizedValueTypeSurrogateCodec<ImmutableArray<T>, ImmutableArraySurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableArrayCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public ImmutableArrayCodec(IValueSerializer<ImmutableArraySurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableArray<T> ConvertFromSurrogate(ref ImmutableArraySurrogate<T> surrogate)
            => surrogate.Values is { } v ? ImmutableArray.Create(v) : default;

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableArray<T> value, ref ImmutableArraySurrogate<T> surrogate)
            => surrogate.Values = value.IsDefault ? null : value.ToArray();
    }

    /// <summary>
    /// <see cref="ImmutableArrayCodec{T}"/>所使用的代金矿类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableArraySurrogate<T>
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public T[]? Values;
    }

    /// <summary>
    /// <see cref="ImmutableArray{T}"/>的复印机。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableArrayCopier<T> : IDeepCopier<ImmutableArray<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ImmutableArrayCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">元素复印机。</param>
        public ImmutableArrayCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        [return: NotNullIfNotNull(nameof(input))]
        object? IDeepCopier.DeepCopy(object? input, CopyContext context)
        {
            if (IsShallowCopyable())
                return input;

            var array = (ImmutableArray<T>)input!;
            return array.IsDefaultOrEmpty ? input : DeepCopy(array, context);
        }

        /// <inheritdoc/>
        public ImmutableArray<T> DeepCopy(ImmutableArray<T> input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            return IsShallowCopyable() || input.IsDefaultOrEmpty ? input : ImmutableArray.CreateRange(input, (i, s) => s._copier.DeepCopy(i, s.context)!, (_copier, context));
        }
    }
}
