using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Orleans.Serialization.Cloning;
using Orleans.Serialization.GeneratedCodeHelpers;
using Orleans.Serialization.Serializers;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="ImmutableStack{T}"/> 的序列化器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableStackCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ImmutableStack<T>, ImmutableStackSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableStackCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public ImmutableStackCodec(IValueSerializer<ImmutableStackSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableStack<T> ConvertFromSurrogate(ref ImmutableStackSurrogate<T> surrogate) => ImmutableStack.CreateRange(Enumerable.Reverse(surrogate.Values));

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableStack<T> value, ref ImmutableStackSurrogate<T> surrogate) => surrogate.Values = new(value);
    }

    /// <summary>
    /// <see cref="ImmutableStackCodec{T}"/> 的代理类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableStackSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public List<T> Values;
    }

    /// <summary>
    /// <see cref="ImmutableStack{T}"/> 的复制器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterCopier]
    public sealed class ImmutableStackCopier<T> : IDeepCopier<ImmutableStack<T>>, IOptionalDeepCopier
    {
        private int _shallowCopyable;
        private readonly IDeepCopier<T> _copier;

        /// <summary>
        /// 初始化 <see cref="ImmutableStackCopier{T}"/> 类的新实例。
        /// </summary>
        /// <param name="copier">元素复制器。</param>
        public ImmutableStackCopier(IDeepCopier<T> copier) => _copier = OrleansGeneratedCodeHelper.UnwrapService(this, copier);

        /// <inheritdoc />
        public bool IsShallowCopyable() => OrleansGeneratedCodeHelper.IsShallowCopyable(
            ref _shallowCopyable, this, static self => OrleansGeneratedCodeHelper.IsShallowCopyable(self._copier));

        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public ImmutableStack<T>? DeepCopy(ImmutableStack<T>? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<ImmutableStack<T>>(input, out var result))
                return result!;

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.IsEmpty || IsShallowCopyable())
                return input;

            // 如果输入集合中有任何值能够参与循环引用，则此处可能发生无限递归。
            // 在这种情况下返回浅拷贝，以缓解该问题。
            context.RecordCopy(input, input);

            var items = new List<T>();
            foreach (var item in input)
                items.Add(_copier.DeepCopy(item, context)!);

            var res = ImmutableStack.CreateRange(items);
            context.RecordCopy(input, res);
            return res;
        }
    }
}
