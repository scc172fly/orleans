namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 包含创建事务状态组件所需的配置。
    /// </summary>
    public class TransactionalStateConfiguration : ITransactionalStateConfiguration
    {
        private readonly string name;
        private readonly string? storage;

        /// <summary>
        /// 初始化 <see cref="TransactionalStateConfiguration"/> 类的新实例。
        /// </summary>
        /// <param name="config">状态和存储配置。</param>
        /// <param name="supportedRoles">该状态所支持的事务参与者角色。</param>
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
        /// 获取该状态所支持的事务参与者角色。
        /// </summary>
        public ParticipantId.Role SupportedRoles { get; }
    }

    /// <summary>
    /// 为 Grain 激活实例创建事务状态组件。
    /// </summary>
    public interface ITransactionalStateFactory
    {
        /// <summary>
        /// 使用指定配置创建事务状态组件。
        /// </summary>
        /// <typeparam name="TState">事务状态类型。</typeparam>
        /// <param name="config">事务状态配置。</param>
        /// <returns>已配置的事务状态组件。</returns>
        ITransactionalState<TState> Create<TState>(TransactionalStateConfiguration config) where TState : class, new();
    }
}
