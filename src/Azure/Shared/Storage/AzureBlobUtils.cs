using System;
using System.Linq;
using System.Text.RegularExpressions;

#if ORLEANS_PERSISTENCE
namespace Orleans.Persistence.AzureStorage
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.AzureStorage
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 与 Azure Blob Storage 相关的通用实用函数。
    /// </summary>
    internal static partial class AzureBlobUtils
    {
        [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.ExplicitCapture | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
        private static partial Regex ContainerNameRegex();

        internal static void ValidateContainerName(string containerName)
        {
            if (string.IsNullOrWhiteSpace(containerName) || containerName.Length < 3 || containerName.Length > 63 || !ContainerNameRegex().IsMatch(containerName))
            {
                throw new ArgumentException("Invalid container name", nameof(containerName));
            }
        }

        internal static void ValidateBlobName(string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName) || blobName.Length > 1024 || blobName.Count(c => c == '/') >= 254)
            {
                throw new ArgumentException("Invalid blob name", nameof(blobName));
            }
        }
    }
}
