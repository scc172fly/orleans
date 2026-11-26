using Microsoft.Extensions.Options;
using Orleans.Persistence.AdoNet.Storage;
using Orleans.Runtime;
using Orleans.Storage;
using System.Data.Common;

namespace Orleans.Configuration
{
    /// <summary>
    /// ADO.NET grain 储存的选项。
    /// </summary>
    public class AdoNetGrainStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// AdoNet 存储的连接字符串。
        /// </summary>
        [Redact]
        public string? ConnectionString { get; set; }

        /// <summary>
        /// 获取或设置打开数据库连接所使用的数据源。
        /// </summary>
        /// <remarks>
        /// 数据源由调用者拥有, Orleans不处置。
        /// </remarks>
        [Redact]
        public DbDataSource? DataSource { get; set; }

        /// <summary>
        /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 存在周期 silo 中的默认的内置阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <summary>
        /// 如果没有给定的话, 存储所使用的默认 ADO.NET 变量。 
        /// </summary>
        public const string DEFAULT_ADONET_INVARIANT = AdoNetInvariants.InvariantNameSqlServer;

        /// <summary>
        /// 存储的变量名称 。
        /// </summary>
        public string Invariant { get; set; } = DEFAULT_ADONET_INVARIANT;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;

        /// <summary>
        /// 获取或设置该存储供应商使用的散装拾取器。 
        /// </summary>
        public IStorageHasherPicker HashPicker { get; set; } = null!;

        /// <summary>
        /// 设置用于此存储提供者使用的与遗留 Orleans v3 兼容的散列拾取器。 如果您需要运行, 请使用该方法
        /// Orleans v7+ silo 相对于现有的 Orleans v3 初始化数据库,并保持现有 grain 状态。
        /// </summary>
        public void UseOrleans3CompatibleHasher()
        {
            // 与不同拾取者一起使用内容意识散列,无法使用标准 StorageHasherPicker
            this.HashPicker = new Orleans3CompatibleStorageHashPicker();
        }

        /// <summary>
        /// 状态清除时删除 Db 的记录行 。
        /// </summary>
        public bool DeleteStateOnClear { get; set; }

        /// <summary>
        /// 获取或设置 grain 状态如何映射为存储密钥。 默认为 <see cref="AdoNetGrainStorageKeyFormat.Legacy"/>。
        /// </summary>
        /// <remarks>
        /// <see cref="AdoNetGrainStorageKeyFormat.GrainTypeQualified"/> 用于新数据库,以便 grains
        /// 使用 <see cref="AdoNetGrainStorageKeyFormat.Legacy"/>
        /// 数据库中已经持有以该格式书写的国家数据。
        /// </remarks>
        public AdoNetGrainStorageKeyFormat KeyFormat { get; set; } = AdoNetGrainStorageKeyFormat.Legacy;
    }

    /// <summary>
    /// ConfigurationValidator 用于 AdoNetGrainStorageOptions
    /// </summary>
    public class AdoNetGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetGrainStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="configurationOptions">选项将生效。</param>
        /// <param name="name">需要验证的选项的名称。</param>
        public AdoNetGrainStorageOptionsValidator(AdoNetGrainStorageOptions configurationOptions, string name)
        {
            this.options = configurationOptions ?? throw new OrleansConfigurationException($"Invalid AdoNetGrainStorageOptions for AdoNetGrainStorage {name}. Options is required.");
            this.name = name;
        }

        /// <inheritdoc cref="IConfigurationValidator"/>
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.Invariant))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainStorageOptions)} values for {nameof(AdoNetGrainStorage)} \"{name}\". {nameof(options.Invariant)} is required.");
            }

            if (string.IsNullOrWhiteSpace(this.options.ConnectionString) == (this.options.DataSource is null))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainStorageOptions)} values for {nameof(AdoNetGrainStorage)} \"{name}\". Configure exactly one of {nameof(options.ConnectionString)} or {nameof(options.DataSource)}.");
            }

            if (this.options.HashPicker == null)
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainStorageOptions)} values for {nameof(AdoNetGrainStorage)} {name}. {nameof(options.HashPicker)} is required.");
            }

            if (!Enum.IsDefined(this.options.KeyFormat))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainStorageOptions)} values for {nameof(AdoNetGrainStorage)} \"{name}\". {nameof(options.KeyFormat)} value {this.options.KeyFormat} is not supported.");
            }

            if (this.options.KeyFormat != AdoNetGrainStorageKeyFormat.Legacy && this.options.HashPicker is Orleans3CompatibleStorageHashPicker)
            {
                throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainStorageOptions)} values for {nameof(AdoNetGrainStorage)} \"{name}\". The Orleans v3-compatible hash picker requires {nameof(options.KeyFormat)} = {nameof(AdoNetGrainStorageKeyFormat.Legacy)}.");
            }
        }
    }

    /// <summary>
    /// 提供默认配置 HashPicker 用于 AdoNetGrainStorageOptions.
    /// </summary>
    public class DefaultAdoNetGrainStorageOptionsHashPickerConfigurator : IPostConfigureOptions<AdoNetGrainStorageOptions>
    {
        /// <summary>
        /// 当未配置散列拾取器时指定默认 Orleans 存储工具。
        /// </summary>
        /// <param name="name">选项实例的名称。</param>
        /// <param name="options">要配置的选项。</param>
        public void PostConfigure(string? name, AdoNetGrainStorageOptions options)
        {
            // 明确保存配置 HashPicker
            if (options.HashPicker != null)
                return;

            // 如果尚未配置, 设置默认值 IHashPicker
            options.HashPicker = new StorageHasherPicker(new[] { new OrleansDefaultHasher() });
        }
    }
}
