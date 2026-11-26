using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableSortedSet{T}"/>的序列器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableSortedSetCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ImmutableSortedSet<T>, ImmutableSortedSetSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableSortedSetCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public ImmutableSortedSetCodec(IValueSerializer<ImmutableSortedSetSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableSortedSet<T> ConvertFromSurrogate(ref ImmutableSortedSetSurrogate<T> surrogate)
            => ImmutableSortedSet.CreateRange(surrogate.KeyComparer, surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableSortedSet<T> value, ref ImmutableSortedSetSurrogate<T> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.KeyComparer = value.KeyComparer != Comparer<T>.Default ? value.KeyComparer : null;
        }
    }

    /// <summary>
    /// <see cref="ImmutableSortedSetCodec{T}"/>所使用的代金矿类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableSortedSetSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public List<T> Values;

        /// <summary>
        /// 获取或设置密钥比较器 。
        /// </summary>
        /// <value>密钥比较器。</value>
        [Id(1)]
        public IComparer<T>? KeyComparer;
    }

    /// <summary>
    /// <see cref="ImmutableSortedSet{T}"/>的复印机。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableSortedSetCopier<T> : IDeepCopier<ImmutableSortedSet<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ImmutableSortedSetCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">元素复印机。</param>
        public ImmutableSortedSetCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ImmutableSortedSet<T>? DeepCopy(ImmutableSortedSet<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ImmutableSortedSet<T>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.IsEmpty || IsShallowCopyable())
                return input;

            // 如果输入收藏的任何值能够参与循环参考, 此处就有可能无限重复。
            // 将这种案件退回浅本,从而减轻这一负担。
            context.RecordCopy(input, input);

            var items = new List<T>(input.Count);
            foreach (var item in input)
                items.Add(_copier.DeepCopy(item, context)!);

            var res = ImmutableSortedSet.CreateRange(input.KeyComparer, items);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
