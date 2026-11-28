using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Persistence.FileStorage;

/// <summary>
/// <see cref="FileGrainStorage"/> 的选项。
/// </summary>
public sealed class FileGrainStorageOptions : IStorageProviderSerializerOptions
{
    /// <summary>
    /// 获取或设置用于存储 Grain 状态的目录。
    /// </summary>
    public required string RootDirectory { get; set; }

    /// <inheritdoc />
    public required IGrainStorageSerializer GrainStorageSerializer { get; set; }
}

internal sealed class FileGrainStorageOptionsValidator(
    FileGrainStorageOptions options,
    string name) : IConfigurationValidator
{
    public void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new OrleansConfigurationException(
                $"Invalid configuration for {nameof(FileGrainStorage)} with name {name}. " +
                $"{nameof(FileGrainStorageOptions)}.{nameof(FileGrainStorageOptions.RootDirectory)} is required.");
        }

        if (File.Exists(options.RootDirectory))
        {
            throw new OrleansConfigurationException(
                $"Invalid configuration for {nameof(FileGrainStorage)} with name {name}. " +
                $"{nameof(FileGrainStorageOptions)}.{nameof(FileGrainStorageOptions.RootDirectory)} must identify a directory.");
        }

    }
}
