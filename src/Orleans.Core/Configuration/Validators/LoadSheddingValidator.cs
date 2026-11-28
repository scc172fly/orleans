using Microsoft.Extensions.Options;
using Orleans.Runtime;

namespace Orleans.Configuration.Validators;

/// <summary>
/// 验证 <see cref="LoadSheddingOptions"/> 的配置。
/// </summary>
/// <remarks>
/// 初始化 <see cref="LoadSheddingValidator"/> 类的新实例。
/// </remarks>
/// <param name="loadSheddingOptions">
/// 负载削减选项。
/// </param>
internal class LoadSheddingValidator(IOptions<LoadSheddingOptions> loadSheddingOptions) : IConfigurationValidator
{
    private readonly LoadSheddingOptions _loadSheddingOptions = loadSheddingOptions.Value;

    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        // 禁用负载削减时，不验证其配置。
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
