using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using Orleans.Storage;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 随机输入存储和状态一致性的故障,导致国家交易性储存业务。
    /// </summary>
    public class RandomErrorInjector : ITransactionFaultInjector
    {
        private readonly double conflictProbability;
        private readonly double beforeProbability;
        private readonly double afterProbability;

        /// <summary>
        /// 初始化 <see cref="RandomErrorInjector"/> 类的新实例。
        /// </summary>
        /// <param name="injectionProbability">储存操作期间注射过错的总概率。</param>
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
        /// 代表随机注入交易存储故障。
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
            /// 从序列数据初始化了 <see cref="RandomlyInjectedStorageException"/> 类的新实例。
            /// </summary>
            /// <param name="info">序列的例外数据。</param>
            /// <param name="context">序列数据的来源或目的地的背景。</param>
            [Obsolete]
            protected RandomlyInjectedStorageException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }

        /// <summary>
        /// 代表随机注入交易状态一致性失灵。
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
            /// 从序列数据初始化了 <see cref="RandomlyInjectedInconsistentStateException"/> 类的新实例。
            /// </summary>
            /// <param name="info">序列的例外数据。</param>
            /// <param name="context">序列数据的来源或目的地的背景。</param>
            [Obsolete]
            protected RandomlyInjectedInconsistentStateException(SerializationInfo info, StreamingContext context)
                : base(info, context)
            {
            }
        }
    }
}
