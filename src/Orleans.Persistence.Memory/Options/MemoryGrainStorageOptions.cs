using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// MemoryGrainStorage 的选项。
    /// </summary>
    public class MemoryGrainStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 队列存储 Grain 的默认数量。
        /// </summary>
        public const int NumStorageGrainsDefaultValue = 10;

        /// <summary>
        /// 获取或设置要使用的存储 Grain 数量。
        /// </summary>
        public int NumStorageGrains { get; set; } = NumStorageGrainsDefaultValue;

        /// <summary>
        /// 获取或设置存储初始化所处的 Silo 生命周期阶段。存储必须在使用前完成初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;

        /// <summary>
        /// 默认初始化阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
    }

    /// <summary>
    /// 验证 <see cref="MemoryGrainStorageOptions"/>。
    /// </summary>
    public class MemoryGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly MemoryGrainStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 初始化 <see cref="MemoryGrainStorageOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项。</param>
        /// <param name="name">名称。</param>
        public MemoryGrainStorageOptionsValidator(MemoryGrainStorageOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }

        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            if (this.options.NumStorageGrains <= 0)
                throw new OrleansConfigurationException(
                    $"Configuration for {nameof(MemoryGrainStorage)} {name} is invalid. {nameof(this.options.NumStorageGrains)} must be larger than 0.");
            if (this.options.InitStage < ServiceLifecycleStage.RuntimeGrainServices)
                throw new OrleansConfigurationException(
                   $"Configuration for {nameof(MemoryGrainStorage)} {name} is invalid. {nameof(this.options.InitStage)} must be larger than {ServiceLifecycleStage.RuntimeGrainServices} since " +
                   $"{nameof(MemoryGrainStorage)} depends on {nameof(MemoryStorageGrain)} to have grain environment to finish set up.");
        }
    }
}
