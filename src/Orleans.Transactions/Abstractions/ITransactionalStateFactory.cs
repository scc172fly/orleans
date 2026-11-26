namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 包含用于创建交易状态元件的配置。
    /// </summary>
    public class TransactionalStateConfiguration : ITransactionalStateConfiguration
    {
        private readonly string name;
        private readonly string? storage;

        /// <summary>
        /// 初始化 <see cref="TransactionalStateConfiguration"/> 类的新实例。
        /// </summary>
        /// <param name="config">状态和储存配置。</param>
        /// <param name="supportedRoles">国家支持的交易参与作用。</param>
        public TransactionalStateConfiguration(ITransactionalStateConfiguration config, ParticipantId.Role supportedRoles = ParticipantId.Role.Resource | ParticipantId.Role.Manager)
        {
            this.name = config.StateName;
            this.storage = config.StorageName;
            this.SupportedRoles = supportedRoles;
        }

        /// <inheritdoc />
        public string StateName => this.name;

        /// <inheritdoc />
        public string? StorageName => this.storage;

        /// <summary>
        /// 获得国家支持的交易参与角色。
        /// </summary>
        public ParticipantId.Role SupportedRoles { get; }
    }

    /// <summary>
    /// 创建用于启动 grain 的交易状态方方面面。
    /// </summary>
    public interface ITransactionalStateFactory
    {
        /// <summary>
        /// 使用指定配置创建交易状态表。
        /// </summary>
        /// <typeparam name="TState">交易状态类型。</typeparam>
        /// <param name="config">交易状态配置。</param>
        /// <returns>配置的交易状态元件。</returns>
        ITransactionalState<TState> Create<TState>(TransactionalStateConfiguration config) where TState : class, new();
    }
}
