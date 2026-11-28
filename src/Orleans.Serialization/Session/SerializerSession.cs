using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using System;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// 序列化器操作的上下文信息。
    /// </summary>
    public sealed class SerializerSession : IDisposable
    {
        /// <summary>
        /// 初始化 <see cref="SerializerSession"/> 类的新实例。
        /// </summary>
        /// <param name="typeCodec">类型编解码器。</param>
        /// <param name="wellKnownTypes">已知类型。</param>
        /// <param name="codecProvider">编解码器提供程序。</param>
        public SerializerSession(TypeCodec typeCodec, WellKnownTypeCollection wellKnownTypes, CodecProvider codecProvider)
        {
            TypeCodec = typeCodec;
            WellKnownTypes = wellKnownTypes;
            CodecProvider = codecProvider;
        }

        /// <summary>
        /// 获取类型编解码器。
        /// </summary>
        /// <value>类型编解码器。</value>
        public TypeCodec TypeCodec { get; }

        /// <summary>
        /// 获取已知类型集合。
        /// </summary>
        /// <value>已知类型集合。</value>
        public WellKnownTypeCollection WellKnownTypes { get; }

        /// <summary>
        /// 获取被引用类型的集合。
        /// </summary>
        /// <value>被引用类型的集合。</value>
        public ReferencedTypeCollection ReferencedTypes { get; } = new ReferencedTypeCollection();

        /// <summary>
        /// 获取被引用对象的集合。
        /// </summary>
        /// <value>被引用对象的集合。</value>
        public ReferencedObjectCollection ReferencedObjects { get; } = new ReferencedObjectCollection();

        /// <summary>
        /// 获取编解码器提供程序。
        /// </summary>
        /// <value>编解码器提供程序。</value>
        public CodecProvider CodecProvider { get; }

        internal Action<SerializerSession>? OnDisposed { get; set; }

        /// <summary>
        /// 重置被引用对象的集合。
        /// </summary>
        public void PartialReset() => ReferencedObjects.Reset();

        /// <summary>
        /// 执行完整重置。
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
