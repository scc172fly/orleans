using System;
using Microsoft.Extensions.ObjectPool;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// <see cref="SerializerSession"/> 对象的对象池。
    /// </summary>
    public sealed class SerializerSessionPool : IDisposable
    {
        private readonly ConcurrentObjectPool<SerializerSession, SerializerSessionPoolPolicy> _sessionPool;

        /// <summary>
        /// 初始化 <see cref="SerializerSessionPool"/> 类的新实例。
        /// </summary>
        /// <param name="typeCodec">类型编解码器。</param>
        /// <param name="wellKnownTypes">已知类型集合。</param>
        /// <param name="codecProvider">编解码器提供程序。</param>
        public SerializerSessionPool(TypeCodec typeCodec, WellKnownTypeCollection wellKnownTypes, CodecProvider codecProvider)
        {
            CodecProvider = codecProvider;
            var sessionPoolPolicy = new SerializerSessionPoolPolicy(typeCodec, wellKnownTypes, codecProvider, ReturnSession);
            _sessionPool = new ConcurrentObjectPool<SerializerSession, SerializerSessionPoolPolicy>(sessionPoolPolicy);
        }

        /// <summary>
        /// 获取编解码器提供程序。
        /// </summary>
        public CodecProvider CodecProvider { get; }

        /// <summary>
        /// 从池中获取序列化器会话。
        /// </summary>
        /// <returns>序列化器会话。</returns>
        public SerializerSession GetSession() => _sessionPool.Get();

        /// <inheritdoc/>
        public void Dispose() => _sessionPool.Dispose();

        /// <summary>
        /// 将会话归还到池中。
        /// </summary>
        /// <param name="session">会话。</param>
        private void ReturnSession(SerializerSession session) => _sessionPool.Return(session);

        private readonly struct SerializerSessionPoolPolicy : IPooledObjectPolicy<SerializerSession>
        {
            private readonly TypeCodec _typeCodec;
            private readonly WellKnownTypeCollection _wellKnownTypes;
            private readonly CodecProvider _codecProvider;
            private readonly Action<SerializerSession> _onSessionDisposed;

            public SerializerSessionPoolPolicy(TypeCodec typeCodec, WellKnownTypeCollection wellKnownTypes, CodecProvider codecProvider, Action<SerializerSession> onSessionDisposed)
            {
                _typeCodec = typeCodec;
                _wellKnownTypes = wellKnownTypes;
                _codecProvider = codecProvider;
                _onSessionDisposed = onSessionDisposed;
            }

            public SerializerSession Create()
            {
                return new SerializerSession(_typeCodec, _wellKnownTypes, _codecProvider)
                {
                    OnDisposed = _onSessionDisposed
                };
            }

            public bool Return(SerializerSession obj)
            {
                obj.Reset();
                return true;
            }
        }
    }
}
