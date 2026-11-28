using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;

//
// 一旦按功能或领域拆分测试项目，就可以减少甚至移除 #if；否则会产生类型歧义及生成错误。
//

#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.AzureStorage
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.AzureStorage
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.AzureStorage
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.AzureStorage
#elif ORLEANS_EVENTHUBS
namespace Orleans.Streaming.EventHubs
#elif TESTER_AZUREUTILS
namespace Orleans.Tests.AzureUtils
#elif ORLEANS_TRANSACTIONS
namespace Orleans.Transactions.AzureStorage
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.AzureStorage
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 与 Azure Table Storage 相关的常量（也适用于 Cosmos DB 的 Table 终结点）。
    /// </summary>
    internal static class AzureTableConstants
    {
        public const string ANY_ETAG = "*";

        public const string PKProperty = nameof(ITableEntity.PartitionKey);
        public const string RKProperty = nameof(ITableEntity.RowKey);

        public const int MaxBatchSize = 100;
    }

    /// <summary>
    /// 与 Azure Table Storage 相关的通用实用函数（也适用于 Cosmos DB 的 Table 终结点）。
    /// </summary>
    internal static partial class AzureTableUtils
    {
        /// <summary>
        /// 值为 "*" 的 ETag，用于在有条件的表操作（更新、合并、删除）中匹配任意 ETag。
        /// </summary>
        public const string ANY_ETAG = AzureTableConstants.ANY_ETAG;

        /// <summary>
        /// 检查 Azure Storage 库返回的异常，判断它是否表示尝试读取存储表中不存在的数据。
        /// </summary>
        /// <param name="exc">Azure Storage 库操作返回的异常。</param>
        /// <returns><c>True</c> 表示此异常意味着要读取的数据在 Azure Table Storage 中不存在。</returns>
        public static bool TableStorageDataNotFound(Exception exc)
        {
            if (EvaluateException(exc, out var httpStatusCode, out var restStatus, true))
            {
                if (IsNotFoundError(httpStatusCode))
                {
                    return true;
                }

                return TableErrorCode.EntityNotFound.ToString().Equals(restStatus, StringComparison.OrdinalIgnoreCase)
                    || TableErrorCode.ResourceNotFound.ToString().Equals(restStatus, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// 从 DataServiceClientException 或 DataServiceQueryException 提取 REST 错误代码。
        /// </summary>
        /// <param name="exc">要检查的异常。</param>
        /// <returns>找到 REST 错误代码时返回该代码，否则返回 <c>null</c></returns>
        private static string? ExtractRestErrorCode(Exception? exc)
        {
            while (exc != null && exc is not RequestFailedException)
            {
                exc = exc.InnerException?.GetBaseException();
            }

            return (exc as RequestFailedException)?.ErrorCode;
        }

        /// <summary>
        /// 检查存储异常，并在适用时提取 HTTP 状态码；如果 <c>getRESTErrors=true</c>，还会提取 REST 错误代码。
        /// </summary>
        /// <param name="e">要检查的异常。</param>
        /// <param name="httpStatusCode">输出适用的 HTTP 状态码，否则输出 HttpStatusCode.Unused (306)。</param>
        /// <param name="restStatus">当 <c>getRESTErrors=true</c> 时，输出适用的 REST 错误代码，否则输出 <c>null</c></param>
        /// <param name="getRESTErrors">是否也应检查和提取 REST 错误代码。</param>
        /// <returns>成功提取 HTTP 状态码和 REST 错误时返回 <c>true</c>。</returns>
        public static bool EvaluateException(
            Exception? e,
            out HttpStatusCode httpStatusCode,
            out string? restStatus,
            bool getRESTErrors = false)
        {
            httpStatusCode = HttpStatusCode.Unused;
            restStatus = null;

            try
            {
                while (e != null)
                {
                    if (e is RequestFailedException ste)
                    {
                        httpStatusCode = (HttpStatusCode)ste.Status;
                        if (getRESTErrors)
                        {
                            restStatus = ExtractRestErrorCode(ste);
                        }
                        return true;
                    }
                    e = e.InnerException;
                }
            }
            catch
            {
                // 如果未能解析异常，则将其视为无法执行 EvaluateException。
                return false;
            }
            return false;
        }

        /// <summary>
        /// 如果指定的 HTTP 状态码或错误代码是在瞬时或可重试错误情况下返回的，则返回 true。
        /// </summary>
        /// <param name="httpStatusCode">HTTP 错误代码值。</param>
        /// <param name="restStatusCode">REST 错误代码值。</param>
        /// <returns><c>true</c> 表示这是瞬时或可重试错误。</returns>
        public static bool IsRetriableHttpError(HttpStatusCode httpStatusCode, string? restStatusCode)
        {
            // 注意：忽略 20X 值，因为它们表示成功结果，而非错误。

            return (
                httpStatusCode == HttpStatusCode.RequestTimeout /* 408 */
                || httpStatusCode == HttpStatusCode.BadGateway          /* 502 */
                || httpStatusCode == HttpStatusCode.ServiceUnavailable  /* 503 */
                || httpStatusCode == HttpStatusCode.GatewayTimeout      /* 504 */
                || (httpStatusCode == HttpStatusCode.InternalServerError /* 500 */
                    && !string.IsNullOrEmpty(restStatusCode)
                    && TableErrorCode.OperationTimedOut.ToString().Equals(restStatusCode, StringComparison.OrdinalIgnoreCase))
            );
        }

        /// <summary>
        /// 检查 REST 调用返回的 HTTP 状态码是否可能由临时的存储竞争错误导致。
        /// </summary>
        /// <param name="httpStatusCode">要检查的 HTTP 状态码。</param>
        /// <returns>HTTP 状态码由存储竞争导致时返回 <c>true</c>。</returns>
        public static bool IsContentionError(HttpStatusCode httpStatusCode)
        {
            // 状态和错误代码。
            // http://msdn.microsoft.com/en-us/library/dd179382.aspx

            if (httpStatusCode == HttpStatusCode.PreconditionFailed) return true;
            if (httpStatusCode == HttpStatusCode.Conflict) return true;     // 主键冲突。应用程序尝试插入实体，但表中已存在 PartitionKey 和 RowKey 值均与待插入实体相同的实体。
            if (httpStatusCode == HttpStatusCode.NotFound) return true;
            if (httpStatusCode == HttpStatusCode.NotImplemented) return true; // 新表：Azure 表架构尚未初始化，因此需要先创建表。
            return false;
        }

        /// <summary>
        /// 检查 REST 调用返回的 HTTP 状态码是否可能由临时的存储竞争错误导致。
        /// </summary>
        /// <param name="httpStatusCode">要检查的 HTTP 状态码。</param>
        /// <returns>HTTP 状态码由存储竞争导致时返回 <c>true</c>。</returns>
        public static bool IsNotFoundError(HttpStatusCode httpStatusCode)
        {
            // 状态和错误代码。
            // http://msdn.microsoft.com/en-us/library/dd179382.aspx

            if (httpStatusCode == HttpStatusCode.NotFound) return true;
            if (httpStatusCode == HttpStatusCode.NotImplemented) return true; // 新表：Azure 表架构尚未初始化，因此需要先创建表。
            return false;
        }

        [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{2,62}$")]
        private static partial Regex TableNameRegex();

        internal static void ValidateTableName(string tableName)
        {
            // 文档中的正则表达式：https://learn.microsoft.com/rest/api/storageservices/understanding-the-table-service-data-model#table-names
            if (!TableNameRegex().IsMatch(tableName))
            {
                throw new ArgumentException($"Table name \"{tableName}\" is invalid according to the following rules:"
                    + " 1. Table names may contain only alphanumeric characters."
                    + " 2. Table names cannot begin with a numeric character."
                    + " 3. Table names must be from 3 to 63 characters long.");
            }
        }

        /// <summary>
        /// 移除 Azure PartitionKey 或 RowKey 值中不允许使用的字符。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string SanitizeTableProperty(string key)
        {
            // 移除 Azure PartitionKey 或 RowKey 值中不允许使用的字符。
            // http://www.jamestharpe.com/web-development/azure-table-service-character-combinations-disallowed-in-partitionkey-rowkey/
            key = key
                .Replace('/', '_')        // 正斜杠。
                .Replace('\\', '_')       // 反斜杠。
                .Replace('#', '_')        // 井号。
                .Replace('?', '_');       // 问号。

            if (key.Length >= 1024)
                throw new ArgumentException(string.Format(
                    CultureInfo.CurrentCulture,
                    "Key length {0} is too long to be an Azure table key. Key={1}",
                    key.Length,
                    key));

            return key;
        }

        internal static string PrintStorageException(Exception exception)
        {
            if (exception is not RequestFailedException storeExc)
            {
                throw new ArgumentException($"Unexpected exception type {exception.GetType().FullName}");
            }

            return $"Message = {storeExc.Message}, HTTP Status = {storeExc.Status}, HTTP Error Code = {storeExc.ErrorCode}.";
        }

        internal static string PointQuery(string partitionKey, string rowKey)
        {
            return TableClient.CreateQueryFilter($"(PartitionKey eq {partitionKey}) and (RowKey eq {rowKey})");
        }

        internal static string RangeQuery(string partitionKey, string minRowKey, string maxRowKey)
        {
            return TableClient.CreateQueryFilter($"((PartitionKey eq {partitionKey}) and (RowKey ge {minRowKey})) and (RowKey le {maxRowKey})");
        }

    }
}
