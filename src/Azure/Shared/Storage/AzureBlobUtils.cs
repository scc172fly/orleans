using System;
using System.Linq;
using System.Text.RegularExpressions;

#if ORLEANS_PERSISTENCE
namespace Orleans.Persistence.AzureStorage
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.AzureStorage
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 与 Azure 布洛布存储有关的一般功用功能。
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
