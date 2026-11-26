using System;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;

//
// #ifs 数量可以减少(或删除),一旦我们按特性/区域分开测试项目,否则最终会出现模糊的类型和错误。
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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 与 Azure 表格存储有关的常数(也适用于宇宙DB中的表端点)。
    /// </summary>
    internal static class AzureTableConstants
    {
        public const string ANY_ETAG = "*";

        public const string PKProperty = nameof(ITableEntity.PartitionKey);
        public const string RKProperty = nameof(ITableEntity.RowKey);

        public const int MaxBatchSize = 100;
    }

    /// <summary>
    /// 与 Azure 表格储存有关的一般功用功能(也适用于宇宙DB中的表端点)。
    /// </summary>
    internal static partial class AzureTableUtils
    {
        /// <summary>
        /// 值ETag "*" ,以匹配任何条件表格操作的egag(更新、合并、删除)。
        /// </summary>
        public const string ANY_ETAG = AzureTableConstants.ANY_ETAG;

        /// <summary>
        /// 检查从 Azure 存储库返回的例外情况,以核实是否试图阅读储存表中不存在的一些数据。
        /// </summary>
        /// <param name="exc">被 Azure 存储库操作</param>
        /// <returns><c>True</c> 如果该例外是指正在阅读的数据没有在 Azure 表格存储表储存</returns>
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
        /// 从 DataServiceClientException 或 DataServiceQueryException提取 REST 错误代码
        /// </summary>
        /// <param name="exc">需要检查的例外。</param>
        /// <returns>如果找到,返回 REST 错误代码, 否则返回 REST 错误代码 <c>null</c></returns>
        private static string? ExtractRestErrorCode(Exception? exc)
        {
            while (exc != null && exc is not RequestFailedException)
            {
                exc = exc.InnerException?.GetBaseException();
            }

            return (exc as RequestFailedException)?.ErrorCode;
        }

        /// <summary>
        /// 审查储存例外,如果适用,将提取HTTP状态代码和 <c>getRESTErrors=true</c>的REST错误代码。
        /// </summary>
        /// <param name="e">需要检查的例外。</param>
        /// <param name="httpStatusCode">输出 HTTP 状态代码(如果适用),否则 HttpStatusCode.Unused (306)</param>
        /// <param name="restStatus">当 <c>getRESTErrors=true</c>]将酌情输出中试源错误代码时,否则 <c>null</c></param>
        /// <param name="getRESTErrors">是否也应检查/提取 REST 错误代码。</param>
        /// <returns>如果 HTTP 状态代码和 REST 错误被提取, 返回真实的 <c>true</c> 。</returns>
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
                // 如果我们没有分析例外, 把它当做我们无法处理 EvaluateException.
                return false;
            }
            return false;
        }

        /// <summary>
        /// 如果指定的 HTTP 状态/错误代码在瞬态/ 可回取错误条件下返回时返回为真
        /// </summary>
        /// <param name="httpStatusCode">HTTP 错误代码值</param>
        /// <param name="restStatusCode">REST 错误代码值</param>
        /// <returns><c>true</c> ,如果这是短暂/可重误</returns>
        public static bool IsRetriableHttpError(HttpStatusCode httpStatusCode, string? restStatusCode)
        {
            // 注:我们忽略20X值,因为它们是成功的结果,而不是错误

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
        /// 检查从 REST 调用返回的 HTTP 状态代码是否是由于存储争议( 临时) 错误所致 。
        /// </summary>
        /// <param name="httpStatusCode">需要检查HTTP状态代码。</param>
        /// <returns>返回 <c>true</c> 如果HTTP状态代码是由于储存争议所致。</returns>
        public static bool IsContentionError(HttpStatusCode httpStatusCode)
        {
            // 状态和错误代码
            // http://msdn.microsoft.com/en-us/library/dd179382.aspx

            if (httpStatusCode == HttpStatusCode.PreconditionFailed) return true;
            if (httpStatusCode == HttpStatusCode.Conflict) return true;     //应用程序试图插入一个实体,但表格上有一个实体,其 PartitionKey 和 RowKey 属性的数值相同。
            if (httpStatusCode == HttpStatusCode.NotFound) return true;
            if (httpStatusCode == HttpStatusCode.NotImplemented) return true; // 新表格: Azure 表预案尚未初始化, 所以需要先创建
            return false;
        }

        /// <summary>
        /// 检查从 REST 调用返回的 HTTP 状态代码是否是由于存储争议( 临时) 错误所致 。
        /// </summary>
        /// <param name="httpStatusCode">需要检查HTTP状态代码。</param>
        /// <returns>返回 <c>true</c> 如果HTTP状态代码是由于储存争议所致。</returns>
        public static bool IsNotFoundError(HttpStatusCode httpStatusCode)
        {
            // 状态和错误代码
            // http://msdn.microsoft.com/en-us/library/dd179382.aspx

            if (httpStatusCode == HttpStatusCode.NotFound) return true;
            if (httpStatusCode == HttpStatusCode.NotImplemented) return true; // 新表格: Azure 表预案尚未初始化, 所以需要先创建
            return false;
        }

        [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{2,62}$")]
        private static partial Regex TableNameRegex();

        internal static void ValidateTableName(string tableName)
        {
            // 文档中的正则表达式: https://learn.microsoft.com/rest/api/storageservices/understanding-the-table-service-data-model#table-names
            if (!TableNameRegex().IsMatch(tableName))
            {
                throw new ArgumentException($"Table name \"{tableName}\" is invalid according to the following rules:"
                    + " 1. Table names may contain only alphanumeric characters."
                    + " 2. Table names cannot begin with a numeric character."
                    + " 3. Table names must be from 3 to 63 characters long.");
            }
        }

        /// <summary>
        /// 删除无法用于 Azure PartitionKey 或 RowKey 值的任何字符。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string SanitizeTableProperty(string key)
        {
            // 删除无法用于 Azure PartitionKey 或 RowKey 数值的任何字符
            // http://www.jamestharpe.com/web-development/azure-table-service-character-combinations-disallowed-in-partitionkey-rowkey/
            key = key
                .Replace('/', '_')        // 向前斜线
                .Replace('\\', '_')       // 反斜斜斜
                .Replace('#', '_')        // 英镑符号
                .Replace('?', '_');       // 问号

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
