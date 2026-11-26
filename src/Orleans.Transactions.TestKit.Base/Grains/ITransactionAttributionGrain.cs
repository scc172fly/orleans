using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义无明确交易归属的 grain 调用。
    /// </summary>
    public interface INoAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义一个 grain 调用来抑制环境交易。
    /// </summary>
    public interface ISuppressAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.Suppress)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义加入环境交易或在没有时创建环境交易的 grain 调用。
    /// </summary>
    public interface ICreateOrJoinAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义创建新交易的 grain 调用。
    /// </summary>
    public interface ICreateAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.Create)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义需要并加入环境交易的 grain 调用。
    /// </summary>
    public interface IJoinAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOptionAlias.Mandatory)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义在存在环境交易且没有环境交易时加入该交易的 grain 调用。
    /// </summary>
    public interface ISupportedAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.Supported)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 定义一个 grain 调用,该调用不交易,拒绝在其中调用。
    /// </summary>
    public interface INotAllowedAttributionGrain : IGrainWithGuidKey
    {
        /// <inheritdoc cref="ITransactionAttributionGrain.GetNestedTransactionIds(int, List{ITransactionAttributionGrain}[])"/>
        [Transaction(TransactionOption.NotAllowed)]
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    #region wrappers

    /// <summary>
    /// 提供 grain 参考文献的通用接口,其中含有不同的交易归属选项。
    /// </summary>
    public interface ITransactionAttributionGrain
    {
        /// <summary>
        /// 记录当前分层观察到的交易识别符号,并依次引用其余的分层。
        /// </summary>
        /// <param name="tier">目前正在执行的零基级。</param>
        /// <param name="tiers">其余的层级,每个元素包含 grains 引用在那一层。</param>
        /// <returns>
        /// 以包含该层级 grains 所观察到的交易识别符的层为单位的列数,按层为人口组的条目编制索引。
        /// <see langword="null"/> 识别符号表示该呼叫没有交易。
        /// </returns>
        Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers);
    }

    /// <summary>
    /// 提供交易归属 grain 参考文件的帮助者和可连号调整器。
    /// </summary>
    public static class TransactionAttributionGrainExtensions
    {
        /// <summary>
        /// 为指定交易选项获取交易属性 grain 适配器。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于解决 grain 参考。</param>
        /// <param name="id"> grain 主键。</param>
        /// <param name="option">
        /// 要应用的交易选项, 或使用 <see langword="null"/> 接口而不明确归属的 grain 交易选项。
        /// </param>
        /// <returns>上方的适配器 grain 对应 <paramref name="option"/>.</returns>
        /// <exception cref="NotSupportedException"><paramref name="option"/> 不是一个支持的交易选项。</exception>
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
        /// 调整 grain 参引,无明确的交易归属。
        /// </summary>
        [GenerateSerializer]
        public class NoAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public INoAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="NoAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
        /// 调整用于抑制环境交易的 grain 参考文献。
        /// </summary>
        [GenerateSerializer]
        public class SuppressAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public ISuppressAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="SuppressAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
        /// 调整 grain 引用,该引用将加入环境交易或在不存在时创建。
        /// </summary>
        [GenerateSerializer]
        public class CreateOrJoinAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public ICreateOrJoinAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="CreateOrJoinAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
        /// 调整创建新交易的 grain 引用。
        /// </summary>
        [GenerateSerializer]
        public class CreateAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public ICreateAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="CreateAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
        /// 调整需要并加入环境交易的 grain 参考文献。
        /// </summary>
        [GenerateSerializer]
        public class JoinAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public IJoinAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="JoinAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
        /// 调整 grain 引用,当存在环境交易时支持该交易。
        /// </summary>
        [GenerateSerializer]
        public class SupportedAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public ISupportedAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="SupportedAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
        /// 调整 grain 参考,以拒绝交易中发出的呼叫。
        /// </summary>
        [GenerateSerializer]
        public class NotAllowedAttributionGrain : ITransactionAttributionGrain
        {
            /// <summary>
            /// 基本 grain 参考。
            /// </summary>
            [Id(0)]
            public INotAllowedAttributionGrain grain;

            /// <summary>
            /// 初始化 <see cref="NotAllowedAttributionGrain"/> 类的新实例。
            /// </summary>
            /// <param name="grain">根本 grain 参考。</param>
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
