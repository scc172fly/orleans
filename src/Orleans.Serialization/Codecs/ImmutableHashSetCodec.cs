using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableHashSet{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableHashSetCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ImmutableHashSet<T>, ImmutableHashSetSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableHashSetCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ImmutableHashSetCodec(IValueSerializer<ImmutableHashSetSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableHashSet<T> ConvertFromSurrogate(ref ImmutableHashSetSurrogate<T> surrogate)
            => ImmutableHashSet.CreateRange(surrogate.KeyComparer, surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableHashSet<T> value, ref ImmutableHashSetSurrogate<T> surrogate)
        {
            surrogate.Values = new(value);
            surrogate.KeyComparer = value.KeyComparer != EqualityComparer<T>.Default ? value.KeyComparer : null;
        }
    }

    /// <summary>
    /// <see cref="ImmutableHashSetCodec{T}"/> 所使用的代理类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableHashSetSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public List<T> Values;

        /// <summary>
        /// 获取或设置键比较器。
        /// </summary>
        /// <value>键比较器。</value>
        [Id(1)]
        public IEqualityComparer<T>? KeyComparer;
    }

    /// <summary>
    /// <see cref="ImmutableHashSet{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableHashSetCopier<T> : IDeepCopier<ImmutableHashSet<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ImmutableHashSetCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">元素复制器。</param>
        public ImmutableHashSetCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ImmutableHashSet<T>? DeepCopy(ImmutableHashSet<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ImmutableHashSet<T>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.IsEmpty || IsShallowCopyable())
                return input;

            // 如果输入集合中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var items = new List<T>(input.Count);
            foreach (var item in input)
                items.Add(_copier.DeepCopy(item, context)!);

            var res = ImmutableHashSet.CreateRange(input.KeyComparer, items);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
