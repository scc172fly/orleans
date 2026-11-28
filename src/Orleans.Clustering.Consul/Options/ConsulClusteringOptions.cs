using System;
using Consul;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// Consul 集群配置选项的基类。
    /// </summary>
    public class ConsulClusteringOptions
    {
        /// <summary>
        /// Consul KV 根目录名称。
        /// </summary>
        public string? KvRootFolder { get; set; }

        /// <summary>
        /// 用于创建 Consul 客户端的工厂。
        /// </summary>
        public Func<IConsulClient> CreateClient { get; private set; }

        /// <summary>
        /// 使用提供的回调配置 <see cref="CreateClient"/>。
        /// </summary>
        public void ConfigureConsulClient(Func<IConsulClient> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 使用 Consul 地址和 ACL 令牌配置 <see cref="CreateClient"/>。
        /// </summary>
        public void ConfigureConsulClient(Uri address, string? aclClientToken = null)
        {
            if (address is null) throw new ArgumentNullException(nameof(address));

            CreateClient = () => new ConsulClient(config =>
            {
                config.Address = address;
                config.Token = aclClientToken;
            });
        }

        /// <summary>
        /// 使用默认 Consul 客户端配置初始化 <see cref="ConsulClusteringOptions"/> 类的新实例。
        /// </summary>
        public ConsulClusteringOptions()
        {
            this.CreateClient = () => new ConsulClient();
        }

        internal void Validate(string? name)
        {
            if (CreateClient is null)
            {
                throw new OrleansConfigurationException($"No callback specified. Use the {GetType().Name}.{nameof(ConsulClusteringOptions.ConfigureConsulClient)} method to configure the consul client.");
            }
        }
    }

    /// <summary>
    /// 验证 Consul 集群成员管理选项。
    /// </summary>
    /// <typeparam name="TOptions">要验证的选项类型。</typeparam>
    public class ConsulClusteringOptionsValidator<TOptions> : IConfigurationValidator where TOptions : ConsulClusteringOptions
    {
        /// <summary>
        /// 初始化 <see cref="ConsulClusteringOptionsValidator{TOptions}"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">已配置的选项名称。</param>
        public ConsulClusteringOptionsValidator(TOptions options, string? name = null)
        {
            Options = options;
            Name = name;
        }

        /// <summary>
        /// 获取要验证的选项。
        /// </summary>
        public TOptions Options { get; }

        /// <summary>
        /// 获取已配置的选项名称。
        /// </summary>
        public string? Name { get; }

        /// <inheritdoc />
        public virtual void ValidateConfiguration()
        {
            Options.Validate(Name);
        }
    }
}
