using System;
using Microsoft.Extensions.ObjectPool;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;

namespace Orleans.Serialization.Session
{
    /// <summary>
    /// <see cref="SerializerSession"/> 对象的集合。
    /// </summary>
    public sealed class SerializerSessionPool : IDisposable
    {
        private readonly ConcurrentObjectPool<SerializerSession, SerializerSessionPoolPolicy> _sessionPool;

        /// <summary>
        /// 初始化 <see cref="SerializerSessionPool"/> 类的新实例。
        /// </summary>
        /// <param name="typeCodec">型号编码器。</param>
        /// <param name="wellKnownTypes">众所周知的类型收藏。</param>
        /// <param name="codecProvider">编码器提供者。</param>
        public SerializerSessionPool(TypeCodec typeCodec, WellKnownTypeCollection wellKnownTypes, CodecProvider codecProvider)
        {
            CodecProvider = codecProvider;
            var sessionPoolPolicy = new SerializerSessionPoolPolicy(typeCodec, wellKnownTypes, codecProvider, ReturnSession);
            _sessionPool = new ConcurrentObjectPool<SerializerSession, SerializerSessionPoolPolicy>(sessionPoolPolicy);
        }

        /// <summary>
        /// 找到编码提供者
        /// </summary>
        public CodecProvider CodecProvider { get; }

        /// <summary>
        /// 从池子里取出一个连环报道片
        /// </summary>
        /// <returns>序列器会话。</returns>
        public SerializerSession GetSession() => _sessionPool.Get();

        /// <inheritdoc/>
        public void Dispose() => _sessionPool.Dispose();

        /// <summary>
        /// 返回池内的一个会话。
        /// </summary>
        /// <param name="session">本届会议。</param>
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
