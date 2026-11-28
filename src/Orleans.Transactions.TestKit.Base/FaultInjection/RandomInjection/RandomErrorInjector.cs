using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Orleans.Storage;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 在事务状态存储操作中随机注入存储失败和状态一致性失败。
    /// </summary>
    public class RandomErrorInjector : ITransactionFaultInjector
    {
        private readonly double conflictProbability;
        private readonly double beforeProbability;
        private readonly double afterProbability;

        /// <summary>
        /// 初始化 <see cref="RandomErrorInjector"/> 类的新实例。
        /// </summary>
        /// <param name="injectionProbability">一次存储操作中注入故障的总概率。</param>
        public RandomErrorInjector(double injectionProbability)
        {
            conflictProbability = injectionProbability / 5;
            beforeProbability = 2 * injectionProbability / 5;
            afterProbability = 2 * injectionProbability / 5;
        }

        /// <inheritdoc />
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "Randomness selects injected storage failures for transaction conformance tests.")]
        public void BeforeStore()
        {
            if (Random.Shared.NextDouble() < conflictProbability)
            {
                throw new RandomlyInjectedInconsistentStateException();
            }
            if (Random.Shared.NextDouble() < beforeProbability)
            {
                throw new RandomlyInjectedStorageException();
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "Randomness selects injected storage failures for transaction conformance tests.")]
        public void AfterStore()
        {
            if (Random.Shared.NextDouble() < afterProbability)
            {
                throw new RandomlyInjectedStorageException();
            }
        }

        /// <summary>
        /// 表示随机注入的事务存储失败。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class RandomlyInjectedStorageException : Exception
        {
            /// <summary>
            /// 初始化 <see cref="RandomlyInjectedStorageException"/> 类的新实例。
            /// </summary>
            public RandomlyInjectedStorageException() : base("injected fault") { }

            /// <summary>
            /// 从序列化数据初始化 <see cref="RandomlyInjectedStorageException"/> 类的新实例。
            /// </summary>
            /// <param name="info">已序列化的异常数据。</param>
            /// <param name="context">有关序列化数据来源或目标的上下文。</param>
            [Obsolete]
            protected RandomlyInjectedStorageException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }

        /// <summary>
        /// 表示随机注入的事务状态一致性失败。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public class RandomlyInjectedInconsistentStateException : InconsistentStateException
        {
            /// <summary>
            /// 初始化 <see cref="RandomlyInjectedInconsistentStateException"/> 类的新实例。
            /// </summary>
            public RandomlyInjectedInconsistentStateException() : base("injected fault") { }

            /// <summary>
            /// 从序列化数据初始化 <see cref="RandomlyInjectedInconsistentStateException"/> 类的新实例。
            /// </summary>
            /// <param name="info">已序列化的异常数据。</param>
            /// <param name="context">有关序列化数据来源或目标的上下文。</param>
            [Obsolete]
            protected RandomlyInjectedInconsistentStateException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }
    }
}
