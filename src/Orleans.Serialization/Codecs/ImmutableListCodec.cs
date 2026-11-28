using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableList{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableListCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ImmutableList<T>, ImmutableListSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableListCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ImmutableListCodec(IValueSerializer<ImmutableListSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableList<T> ConvertFromSurrogate(ref ImmutableListSurrogate<T> surrogate) => ImmutableList.CreateRange(surrogate.Values);

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableList<T> value, ref ImmutableListSurrogate<T> surrogate) => surrogate.Values = new(value);
    }

    /// <summary>
    /// <see cref="ImmutableListCodec{T}"/> 所使用的代理类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableListSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public List<T> Values;
    }

    /// <summary>
    /// <see cref="ImmutableList{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableListCopier<T> : IDeepCopier<ImmutableList<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ImmutableListCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">元素复制器。</param>
        public ImmutableListCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ImmutableList<T>? DeepCopy(ImmutableList<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ImmutableList<T>>(input, out var result))
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

            var res = ImmutableList.CreateRange(items);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
