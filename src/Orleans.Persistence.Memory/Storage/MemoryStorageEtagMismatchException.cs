using System;
using System.Runtime.Serialization;

namespace Orleans.Storage.Internal
{
    /// <summary>用于与存储供应商沟通的例外,以便将其例外交给其调用者。</summary>
    [Serializable]
    [GenerateSerializer]
    internal sealed class MemoryStorageEtagMismatchException : Exception
    {
        /// <summary>获取当前持续存储中的静态值 。</summary>
        [Id(0)]
        public string? StoredEtag { get; private set; }

        /// <summary>获取当前在记忆中帮助存储的静态值,并试图更新。</summary>
        [Id(1)]
        public string? ReceivedEtag { get; private set; }

        /// <summary>
        /// 初始化 <see cref="MemoryStorageEtagMismatchException"/> 类的新实例。
        /// </summary>
        /// <param name="storedEtag">存储的etag。</param>
        /// <param name="receivedEtag">接收的目录。</param>
        public MemoryStorageEtagMismatchException(string storedEtag, string? receivedEtag)
        {
            StoredEtag = storedEtag;
            ReceivedEtag = receivedEtag;
        }

        /// <summary>
        /// 初始化 <see cref="MemoryStorageEtagMismatchException"/> 类的新实例。
        /// </summary>
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
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
        /// <returns>一个新的 <see cref="InconsistentStateException"/>。</returns>
        public InconsistentStateException AsInconsistentStateException()
        {
            var message = $"e-Tag mismatch in Memory Storage. Stored = {StoredEtag ?? "null"} Received = {ReceivedEtag}";
            return new InconsistentStateException(message, StoredEtag, ReceivedEtag, this);
        }
    }
}
