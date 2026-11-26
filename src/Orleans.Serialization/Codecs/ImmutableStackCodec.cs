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
    /// <see cref="ImmutableStack{T}"/>的序列器。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [RegisterSerializer]
    public sealed class ImmutableStackCodec<T> : GeneralizedReferenceTypeSurrogateCodec<ImmutableStack<T>, ImmutableStackSurrogate<T>>
    {
        /// <summary>
        /// 初始化 <see cref="ImmutableStackCodec{T}"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代孕序列器</param>
        public ImmutableStackCodec(IValueSerializer<ImmutableStackSurrogate<T>> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override ImmutableStack<T> ConvertFromSurrogate(ref ImmutableStackSurrogate<T> surrogate) => ImmutableStack.CreateRange(Enumerable.Reverse(surrogate.Values));

        /// <inheritdoc/>
        public override void ConvertToSurrogate(ImmutableStack<T> value, ref ImmutableStackSurrogate<T> surrogate) => surrogate.Values = new(value);
    }

    /// <summary>
    /// <see cref="ImmutableStackCodec{T}"/>的代用类型。
    /// </summary>
    /// <typeparam name="T">元素类型。</typeparam>
    [GenerateSerializer]
    public struct ImmutableStackSurrogate<T>
    {
        /// <summary>
        /// 获取或设置值。
        /// </summary>
        /// <value>价值。</value>
        [Id(0)]
        public List<T> Values;
    }

    /// <summary>
    /// <see cref="ImmutableStack{T}"/>的复印机。
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
        /// <param name="copier">元素复印机。</param>
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

            // 如果输入收藏的任何值能够参与循环参考, 此处就有可能无限重复。
            // 将这种案件退回浅本,从而减轻这一负担。
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
