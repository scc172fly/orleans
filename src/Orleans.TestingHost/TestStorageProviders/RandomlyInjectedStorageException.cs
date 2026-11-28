using Orleans.Storage;
using System;
using System.Runtime.Serialization;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 表示随机注入的存储异常。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class RandomlyInjectedStorageException : Exception
    {
        /// <summary>
        /// 初始化 <see cref="RandomlyInjectedStorageException"/> 类的新实例。
        /// </summary>
        public RandomlyInjectedStorageException() : base("injected fault") { }

        /// <summary>
        /// 初始化 <see cref="RandomlyInjectedStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="info">包含所抛出异常的序列化对象数据的 <see cref="T:System.Runtime.Serialization.SerializationInfo" />。</param>
        /// <param name="context">包含源或目标上下文信息的 <see cref="T:System.Runtime.Serialization.StreamingContext" />。</param>
        [Obsolete]
        private RandomlyInjectedStorageException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 表示随机注入的 <see cref="InconsistentStateException"/>。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class RandomlyInjectedInconsistentStateException : InconsistentStateException
    {
        /// <summary>
        /// 初始化 <see cref="RandomlyInjectedInconsistentStateException"/> 类的新实例。
        /// </summary>
        public RandomlyInjectedInconsistentStateException() : base("injected fault") { }

        /// <summary>
        /// 初始化 <see cref="RandomlyInjectedInconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private RandomlyInjectedInconsistentStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
