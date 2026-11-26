using Orleans.Storage;
using System;
using System.Runtime.Serialization;

namespace Orleans.TestingHost
{
    /// <summary>
    /// 代表随机注射储存例外。
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
        /// <param name="info">下 <see cref="T:System.Runtime.Serialization.SerializationInfo" /> 保存关于被抛出例外的序列物体数据。</param>
        /// <param name="context">下 <see cref="T:System.Runtime.Serialization.StreamingContext" /> 包含源或目的地的背景资料。</param>
        [Obsolete]
        private RandomlyInjectedStorageException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// 代表随机注入的 <see cref="InconsistentStateException"/>。
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
        /// <param name="info">序列号信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        private RandomlyInjectedInconsistentStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
