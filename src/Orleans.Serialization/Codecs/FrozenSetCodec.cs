#if NET8_0_OR_GREATER
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="FrozenSet{T}"/>的序列器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class FrozenSetCodec<T> : GeneralizedReferenceTypeSurrogateCodec<FrozenSet<T>, FrozenSetSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="FrozenSetCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public FrozenSetCodec(IValueSerializer<FrozenSetSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override FrozenSet<T> ConvertFromSurrogate(ref FrozenSetSurrogate<T> surrogate)
            => surrogate.Values.ToFrozenSet(surrogate.KeyComparer);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(FrozenSet<T> value, ref FrozenSetSurrogate<T> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.KeyComparer = value.Comparer != EqualityComparer<T>.Default ? value.Comparer : null;
        }
    }

    /// <summary>
    /// <see cref="FrozenSetCodec{T}"/>所使用的代金矿类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct FrozenSetSurrogate<T>
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
        public IEqualityComparer<T>? KeyComparer;
    }

    /// <summary>
    /// <see cref="FrozenSet{T}"/>的复印机。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class FrozenSetCopier<T> : IDeepCopier<FrozenSet<T>>, IOptionalDeepCopier, IDerivedTypeCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="FrozenSetCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">元素复印机。</param>
        public FrozenSetCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public FrozenSet<T>? DeepCopy(FrozenSet<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<FrozenSet<T>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.Count == 0 || IsShallowCopyable())
                return input;

            // 如果输入收藏的任何值能够参与循环参考, 此处就有可能无限重复。
            // 将这种案件退回浅本,从而减轻这一负担。
            context.RecordCopy(input, input);

            var items = new List<T>(input.Count);
            foreach (var item in input)
                items.Add(_copier.DeepCopy(item, context)!);

            var res = items.ToFrozenSet(input.Comparer);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
#endif
