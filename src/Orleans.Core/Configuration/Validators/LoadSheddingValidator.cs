using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration.Validators;

/// <summary>
/// 校验 <see cref="LoadSheddingOptions"/> 配置。
/// </summary>
/// <remarks>
/// 初始化 <see cref="LoadSheddingValidator"/> 类的新实例。
/// </remarks>
/// <param name="loadSheddingOptions">
/// 负荷叠叠选项 。
/// </param>
internal class LoadSheddingValidator(IOptions<LoadSheddingOptions> loadSheddingOptions) : IConfigurationValidator
{
    private readonly LoadSheddingOptions _loadSheddingOptions = loadSheddingOptions.Value;

    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        // 当装入 Shedding 禁用时, 不要验证配置 。
        if (!_loadSheddingOptions.LoadSheddingEnabled)
        {
            return;
        }

        if (_loadSheddingOptions.CpuThreshold > 100 || _loadSheddingOptions.CpuThreshold <= 0)
        {
            throw new OrleansConfigurationException($"Limit '{nameof(LoadSheddingOptions)}.{nameof(LoadSheddingOptions.CpuThreshold)}' must be greater than 0% and less than or equal to 100%.");
        }

        if (_loadSheddingOptions.MemoryThreshold > 100 || _loadSheddingOptions.MemoryThreshold <= 0)
        {
            throw new OrleansConfigurationException($"Limit '{nameof(LoadSheddingOptions)}.{nameof(LoadSheddingOptions.MemoryThreshold)}' must be greater than 0% and less than or equal to 100%.");
        }
    }
}
