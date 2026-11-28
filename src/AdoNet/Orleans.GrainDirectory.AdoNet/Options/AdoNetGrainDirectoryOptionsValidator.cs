using static System.String;

namespace Orleans.Configuration;

/// <summary>
/// 验证 <see cref="AdoNetGrainDirectoryOptions"/> 配置。
/// </summary>
/// <param name="options">要验证的选项。</param>
/// <param name="name">Grain 目录名称。</param>
public class AdoNetGrainDirectoryOptionsValidator(AdoNetGrainDirectoryOptions options, string name) : IConfigurationValidator
{
    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        if (options is null)
        {
            throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainDirectoryOptions)} values for {nameof(AdoNetGrainDirectory)}|{name}. {nameof(options)} is required.");
        }

        if (IsNullOrWhiteSpace(options.Invariant))
        {
            throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainDirectoryOptions)} values for {nameof(AdoNetGrainDirectory)}|{name}. {nameof(options.Invariant)} is required.");
        }

        if (IsNullOrWhiteSpace(options.ConnectionString) == (options.DataSource is null))
        {
            throw new OrleansConfigurationException($"Invalid {nameof(AdoNetGrainDirectoryOptions)} values for {nameof(AdoNetGrainDirectory)}|{name}. Configure exactly one of {nameof(options.ConnectionString)} or {nameof(options.DataSource)}.");
        }
    }
}
