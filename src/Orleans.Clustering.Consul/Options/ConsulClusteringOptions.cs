using System;
using Consul;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 领事-集群备选方案的基础类。
    /// </summary>
    public class ConsulClusteringOptions
    {
        /// <summary>
        /// 领事 KV 根文件夹名 。
        /// </summary>
        public string? KvRootFolder { get; set; }

        /// <summary>
        /// 旧领事服务员工厂
        /// </summary>
        public Func<IConsulClient> CreateClient { get; private set; }

        /// <summary>
        /// 使用所提供的回调配置 <see cref="CreateClient"/> 。
        /// </summary>
        public void ConfigureConsulClient(Func<IConsulClient> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 使用领事地址和按键配置 <see cref="CreateClient"/> 。
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
        /// 使用默认的领事客户配置初始化 <see cref="ConsulClusteringOptions"/> 类的新实例。
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
    /// 核实顾问集群备选方案。
    /// </summary>
    /// <typeparam name="TOptions">验证的选项类型。</typeparam>
    public class ConsulClusteringOptionsValidator<TOptions> : IConfigurationValidator where TOptions : ConsulClusteringOptions
    {
        /// <summary>
        /// 初始化 <see cref="ConsulClusteringOptionsValidator{TOptions}"/> 类的新实例。
        /// </summary>
        /// <param name="options">验证的选项。</param>
        /// <param name="name">配置的选项名称。</param>
        public ConsulClusteringOptionsValidator(TOptions options, string? name = null)
        {
            Options = options;
            Name = name;
        }

        /// <summary>
        /// 获取选项来验证 。
        /// </summary>
        public TOptions Options { get; }

        /// <summary>
        /// 获取配置的选项名称。
        /// </summary>
        public string? Name { get; }

        /// <inheritdoc />
        public virtual void ValidateConfiguration()
        {
            Options.Validate(Name);
        }
    }
}
