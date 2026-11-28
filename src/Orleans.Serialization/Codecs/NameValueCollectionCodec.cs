using Orleans.Serialization.Cloning;
using Orleans.Serialization.Serializers;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Orleans.Serialization.Codecs
{
    /// <summary>
    /// <see cref="NameValueCollection"/> 的序列化器。
    /// </summary>
    [RegisterSerializer]
    public sealed class NameValueCollectionCodec : GeneralizedReferenceTypeSurrogateCodec<NameValueCollection, NameValueCollectionSurrogate>
    {
        /// <summary>
        /// 初始化 <see cref="NameValueCollectionCodec"/> 类的新实例。
        /// </summary>
        /// <param name="surrogateSerializer">代理类型序列化器。</param>
        public NameValueCollectionCodec(IValueSerializer<NameValueCollectionSurrogate> surrogateSerializer) : base(surrogateSerializer)
        {
        }

        /// <inheritdoc/>
        public override NameValueCollection ConvertFromSurrogate(ref NameValueCollectionSurrogate surrogate)
        {
            var result = new NameValueCollection(surrogate.Values?.Count ?? 0);
            if (surrogate.Values is { } values)
            {
                foreach (var value in values)
                {
                    result.Add(value.Key, value.Value);
                }
            }

            if (surrogate.HasNullKey)
            {
                result.Add(null, surrogate.NullKeyValue);
            }

            return result;
        }

        /// <inheritdoc/>
        public override void ConvertToSurrogate(NameValueCollection value, ref NameValueCollectionSurrogate surrogate)
        {
            var result = new Dictionary<string, string?>(value.Count);
            surrogate.HasNullKey = false;
            surrogate.NullKeyValue = null;
            for (var i = 0; i < value.Count; i++)
            {
                var key = value.GetKey(i);
                if (key is null)
                {
                    surrogate.HasNullKey = true;
                    surrogate.NullKeyValue = value.Get(i);
                }
                else
                {
                    result.Add(key, value.Get(i));
                }
            }

            surrogate.Values = result;
        }
    }

    /// <summary>
    /// <see cref="NameValueCollectionCodec"/> 所使用的代理类型。
    /// </summary>
    [GenerateSerializer]
    public struct NameValueCollectionSurrogate
    {
        /// <summary>
        /// 获取或设置值集合。
        /// </summary>
        /// <value>值集合。</value>
        [Id(0)]
        public Dictionary<string, string?>? Values;

        /// <summary>
        /// 获取或设置一个值，指示集合是否包含 null 键。
        /// </summary>
        [Id(1)]
        public bool HasNullKey;

        /// <summary>
        /// 获取或设置与 null 键关联的值。
        /// </summary>
        [Id(2)]
        public string? NullKeyValue;
    }

    /// <summary>
    /// <see cref="NameValueCollection"/> 的复制器。
    /// </summary>
    [RegisterCopier]
    public sealed class NameValueCollectionCopier : IDeepCopier<NameValueCollection>
    {
        /// <inheritdoc/>
        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(input))]
        public NameValueCollection? DeepCopy(NameValueCollection? input, CopyContext context)
        {
            ArgumentNullExceptionPolyfill.ThrowIfNull(context);

            if (context.TryGetCopy<NameValueCollection>(input, out var result))
            {
                return result!;
            }

            System.Diagnostics.Debug.Assert(input is not null);
            if (input.GetType() != typeof(NameValueCollection))
            {
                return context.DeepCopy(input);
            }

            result = new NameValueCollection(input.Count);
            context.RecordCopy(input, result);
            for (var i = 0; i < input.Count; i++)
            {
                result.Add(input.GetKey(i), input.Get(i));
            }

            return result;
        }
    }
}
