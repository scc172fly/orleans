using System;
using System.Runtime.Serialization;

namespace Orleans.Storage.Internal
{
    /// <summary>用于与存储提供程序通信的异常，使存储提供程序向其调用方抛出该异常。</summary>
    [Serializable]
    [GenerateSerializer]
    internal sealed class MemoryStorageEtagMismatchException : Exception
    {
        /// <summary>获取当前保存在持久化存储中的 ETag 值。</summary>
        [Id(0)]
        public string? StoredEtag { get; private set; }

        /// <summary>获取当前保存在内存中并尝试更新的 ETag 值。</summary>
        [Id(1)]
        public string? ReceivedEtag { get; private set; }

        /// <summary>
        /// 初始化 <see cref="MemoryStorageEtagMismatchException"/> 类的新实例。
        /// </summary>
        /// <param name="storedEtag">已存储的 ETag。</param>
        /// <param name="receivedEtag">接收到的 ETag。</param>
        public MemoryStorageEtagMismatchException(string storedEtag, string? receivedEtag)
        {
            StoredEtag = storedEtag;
            ReceivedEtag = receivedEtag;
        }

        /// <summary>
        /// 初始化 <see cref="MemoryStorageEtagMismatchException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
        [Obsolete]
        private MemoryStorageEtagMismatchException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.StoredEtag = info.GetString(nameof(StoredEtag));
            this.ReceivedEtag = info.GetString(nameof(ReceivedEtag));
        }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(StoredEtag), this.StoredEtag);
            info.AddValue(nameof(ReceivedEtag), this.ReceivedEtag);
            base.GetObjectData(info, context);
        }

        /// <summary>
        /// 将此实例转换为 <see cref="InconsistentStateException"/>。
        /// </summary>
        /// <returns>新的 <see cref="InconsistentStateException"/>。</returns>
        public InconsistentStateException AsInconsistentStateException()
        {
            var message = $"e-Tag mismatch in Memory Storage. Stored = {StoredEtag ?? "null"} Received = {ReceivedEtag}";
            return new InconsistentStateException(message, StoredEtag, ReceivedEtag, this);
        }
    }
}
