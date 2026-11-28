using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义没有显式事务特性标注的 Grain 调用。
    /// </summary>
    public interface INoAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义抑制环境事务的 Grain 调用。
    /// </summary>
    public interface ISuppressAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.Suppress)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义加入环境事务、在没有环境事务时创建事务的 Grain 调用。
    /// </summary>
    public interface ICreateOrJoinAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义创建新事务的 Grain 调用。
    /// </summary>
    public interface ICreateAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.Create)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义要求存在环境事务并加入该事务的 Grain 调用。
    /// </summary>
    public interface IJoinAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOptionAlias.Mandatory)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义环境事务存在时加入该事务、否则在无事务情况下运行的 Grain 调用。
    /// </summary>
    public interface ISupportedAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.Supported)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义在无事务情况下运行并拒绝在事务内发起的调用的 Grain 调用。
    /// </summary>
    public interface INotAllowedAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.NotAllowed)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    #region wrappers

    /// <summary>
    /// 为具有不同事务特性选项的 Grain 引用提供公共接口。
    /// </summary>
    public interface ITransactionAttributionGrain
    {
        /// <summary>
        /// 记录当前层观测到的事务标识符，并递归调用其余各层。
        /// </summary>
        /// <param name="tier">当前正在执行的层的索引（从零开始）。</param>
        /// <param name="tiers">其余各层，其中每个元素包含在该层调用的 Grain。</param>
        /// <returns>
        /// 按层索引的数组，其中已填充的条目包含该层 Grain 观测到的事务标识符。
        /// 标识符为 <see langword="null"/> 表示调用在无事务情况下运行。
        /// </returns>
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 为带事务特性的 Grain 引用提供辅助方法和可序列化的适配器。
    /// </summary>
    public static class TransactionAttributionGrainExtensions
    {
        /// <summary>
        /// 获取对应于指定事务选项的事务特性 Grain 适配器。
        /// </summary>
        /// <param name="grainFactory">用于解析 Grain 引用的 Grain 工厂。</param>
        /// <param name="id">Grain 主键。</param>
        /// <param name="option">
        /// 要应用的事务选项；指定 <see langword="null"/> 则使用没有显式事务特性标注的 Grain 接口。
        /// </param>
        /// <returns>对应于 <paramref name="option"/> 的 Grain 接口适配器。</returns>
        /// <exception cref="NotSupportedException"><paramref name="option"/> 不是受支持的事务选项。</exception>
        public static ITransactionAttributionGrain GetTransactionAttributionGrain(this IGrainFactory grainFactory, Guid id, TransactionOption? option = null)
        {
            ArgumentNullException.ThrowIfNull(grainFactory);

            if (!option.HasValue)
            {
                return new NoAttributionGrain(grainFactory.GetGrain<INoAttributionGrain>(id));
            }
            switch (option.Value)
            {
                case TransactionOption.Suppress:
                    return new SuppressAttributionGrain(grainFactory.GetGrain<ISuppressAttributionGrain>(id));
                case TransactionOption.CreateOrJoin:
                    return new CreateOrJoinAttributionGrain(grainFactory.GetGrain<ICreateOrJoinAttributionGrain>(id));
                case TransactionOption.Create:
                    return new CreateAttributionGrain(grainFactory.GetGrain<ICreateAttributionGrain>(id));
                case TransactionOption.Join:
                    return new JoinAttributionGrain(grainFactory.GetGrain<IJoinAttributionGrain>(id));
                case TransactionOption.Supported:
                    return new SupportedAttributionGrain(grainFactory.GetGrain<ISupportedAttributionGrain>(id));
                case TransactionOption.NotAllowed:
                    return new NotAllowedAttributionGrain(grainFactory.GetGrain<INotAllowedAttributionGrain>(id));
                default:
                    throw new NotSupportedException($"Transaction option {option.Value} is not supported.");
            }
        }

        /// <summary>
        /// 适配没有显式事务特性标注的 Grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class NoAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public INoAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="NoAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public NoAttributionGrain(INoAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }

        /// <summary>
        /// 适配抑制环境事务的 Grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class SuppressAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public ISuppressAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="SuppressAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public SuppressAttributionGrain(ISuppressAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }

        /// <summary>
        /// 适配加入环境事务、在没有环境事务时创建事务的 Grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class CreateOrJoinAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public ICreateOrJoinAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="CreateOrJoinAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public CreateOrJoinAttributionGrain(ICreateOrJoinAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }

        /// <summary>
        /// 适配创建新事务的 Grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class CreateAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public ICreateAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="CreateAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public CreateAttributionGrain(ICreateAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }

        /// <summary>
        /// 适配要求存在环境事务并加入该事务的 Grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class JoinAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public IJoinAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="JoinAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public JoinAttributionGrain(IJoinAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }

        /// <summary>
        /// 适配在环境事务存在时支持该事务的 Grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class SupportedAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public ISupportedAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="SupportedAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public SupportedAttributionGrain(ISupportedAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }

        /// <summary>
        /// 适配 Grain 引用，该引用拒绝从事务内发起的调用。
        /// </summary>
        [GenerateSerializer]
        public class NotAllowedAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 底层 Grain 引用。
            /// </summary>
            [Id(0)]
            public INotAllowedAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="NotAllowedAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">底层 Grain 引用。</param>
            public NotAllowedAttributionGrain(INotAllowedAttributionGrain grain)
            {
                this.grain = grain;
            }

            /// <inheritdoc/>
            public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
            {
                return this.grain.GetNestedTransactionIds(tier, tiers);
            }
        }
    }
    #endregion wrappers
}
