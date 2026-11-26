using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using System;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// 序列器操作的背景信息。
    /// </summary>
    public sealed class SerializerSession : IDisposable
    {
        /// <summary>
        /// 初始化 <see cref="SerializerSession"/> 类的新实例。
        /// </summary>
        /// <param name="typeCodec">型号编码器。</param>
        /// <param name="wellKnownTypes">众所周知的类型。</param>
        /// <param name="codecProvider">编码器提供者。</param>
        public SerializerSession(TypeCodec typeCodec, WellKnownTypeCollection wellKnownTypes, CodecProvider codecProvider)
        {
            TypeCodec = typeCodec;
            WellKnownTypes = wellKnownTypes;
            CodecProvider = codecProvider;
        }

        /// <summary>
        /// 获得型号代码。
        /// </summary>
        /// <value>型号编码器。</value>
        public TypeCodec TypeCodec { get; }

        /// <summary>
        /// 获得众所周知的种类收藏。
        /// </summary>
        /// <value>众所周知的类型收藏。</value>
        public WellKnownTypeCollection WellKnownTypes { get; }

        /// <summary>
        /// 获取引用的类型收藏。
        /// </summary>
        /// <value>引用的类型收藏。</value>
        public ReferencedTypeCollection ReferencedTypes { get; } = new ReferencedTypeCollection();

        /// <summary>
        /// 获取引用对象收藏。
        /// </summary>
        /// <value>引用的对象收藏。</value>
        public ReferencedObjectCollection ReferencedObjects { get; } = new ReferencedObjectCollection();

        /// <summary>
        /// 找到编码提供者
        /// </summary>
        /// <value>编码器提供者。</value>
        public CodecProvider CodecProvider { get; }

        internal Action<SerializerSession>? OnDisposed { get; set; }

        /// <summary>
        /// 重置引用对象收藏。
        /// </summary>
        public void PartialReset() => ReferencedObjects.Reset();

        /// <summary>
        /// 执行全重置。
        /// </summary>
        public void Reset()
        {
            ReferencedObjects.Reset();
            ReferencedTypes.Reset();
        }

        /// <inheritdoc/>
        public void Dispose() => OnDisposed?.Invoke(this);
    }
}
