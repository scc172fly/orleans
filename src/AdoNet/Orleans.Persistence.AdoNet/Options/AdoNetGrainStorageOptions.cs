using Microsoft.Extensions.Options;
using Orleans.Persistence.AdoNet.Storage;
using Orleans.Runtime;
using Orleans.Storage;
using System.Data.Common;

namespace Orleans.Configuration
{
    /// <summary>
    /// ADO.NET Grain 存储选项。
    /// </summary>
    public class AdoNetGrainStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// ADO.NET 存储的连接字符串。
        /// </summary>
        [Redact]
        public string? ConnectionString { get; set; }

        /// <summary>
        /// 获取或设置用于打开数据库连接的数据源。
        /// </summary>
        /// <remarks>
        /// 数据源由调用方拥有，Orleans 不负责释放它。
        /// </remarks>
        [Redact]
        public DbDataSource? DataSource { get; set; }

        /// <summary>
        /// 应初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// Silo 生命周期的默认初始化阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <summary>
        /// 未指定时使用的默认存储 ADO.NET 固定名称（invariant name）。
        /// </summary>
        public const string DEFAULT_ADONET_INVARIANT = AdoNetInvariants.InvariantNameSqlServer;

        /// <summary>
        /// 存储的固定名称（invariant name）。
        /// </summary>
        public string Invariant { get; set; } = DEFAULT_ADONET_INVARIANT;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;

        /// <summary>
        /// 获取或设置此存储提供程序使用的哈希算法选择器。
        /// </summary>
        public IStorageHasherPicker HashPicker { get; set; } = null!;

        /// <summary>
        /// 为此存储提供程序设置与旧版 Orleans v3 兼容的哈希算法选择器。如果需要使用
        /// Orleans v7+ Silo 访问由 Orleans v3 初始化的现有数据库并保留原 Grain 状态，请调用此方法。
        /// </summary>
        public void UseOrleans3CompatibleHasher()
        {
            // 哈希计算需要感知内容并使用不同的选择器，无法使用标准 StorageHasherPicker。
            this.HashPicker = new Orleans3CompatibleStorageHashPicker();
        }

        /// <summary>
        /// 清除状态时从数据库删除对应的记录行。
        /// </summary>
        public bool DeleteStateOnClear { get; set; }

        /// <summary>
        /// 获取或设置将 Grain 状态映射为存储键的方式。默认为 <see cref="AdoNetGrainStorageKeyFormat.Legacy"/>。
        /// </summary>
        /// <remarks>
        /// 对于新数据库，使用 <see cref="AdoNetGrainStorageKeyFormat.GrainTypeQualified"/>，以便键相同而
        /// 类型不同的 Grain 具有各自独立的状态。对于已有使用旧格式写入状态的数据库，保留 <see cref="AdoNetGrainStorageKeyFormat.Legacy"/>
        /// 格式。
        /// </remarks>
        public AdoNetGrainStorageKeyFormat KeyFormat { get; set; } = AdoNetGrainStorageKeyFormat.Legacy;
    }

    /// <summary>
    /// AdoNetGrainStorageOptions 的配置验证器。
    /// </summary>
    public class AdoNetGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly AdoNetGrainStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="configurationOptions">要验证的选项。</param>
        /// <param name="name">要验证的选项的名称。</param>
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
    /// 为 AdoNetGrainStorageOptions 提供默认 HashPicker 配置。
    /// </summary>
    public class DefaultAdoNetGrainStorageOptionsHashPickerConfigurator : IPostConfigureOptions<AdoNetGrainStorageOptions>
    {
        /// <summary>
        /// 如果未配置哈希算法选择器，则分配默认的 Orleans 存储哈希算法。
        /// </summary>
        /// <param name="name">选项实例的名称。</param>
        /// <param name="options">要配置的选项。</param>
        public void PostConfigure(string? name, AdoNetGrainStorageOptions options)
        {
            // 保留显式配置的 HashPicker。
            if (options.HashPicker != null)
                return;

            // 如果尚未配置 IHashPicker，则设置默认值。
            options.HashPicker = new StorageHasherPicker(new[] { new OrleansDefaultHasher() });
        }
    }
}
