using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;
using Orleans.AzureUtils.Utilities;
using Orleans.Configuration;

namespace Orleans.AzureUtils
{
    internal interface IAzureQueueDataManager
    {
        Task InitQueueAsync();

        Task<IEnumerable<QueueMessage>> GetQueueMessages(int? count = null);

        Task DeleteQueueMessage(QueueMessage message);

        Task ReleaseQueueMessage(QueueMessage message, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 如何使用 Queue Storage 服务：http://www.windowsazure.com/en-us/develop/net/how-to-guides/queue-service/
    /// Windows Azure Storage 抽象及其可伸缩性目标：http://blogs.msdn.com/b/windowsazurestorage/archive/2010/05/10/windows-azure-storage-abstractions-and-their-scalability-targets.aspx
    /// 队列与元数据命名：http://msdn.microsoft.com/en-us/library/windowsazure/dd179349.aspx
    /// Windows Azure Queue 与 Windows Azure Service Bus Queue 对比：http://msdn.microsoft.com/en-us/library/hh767287(VS.103).aspx
    /// 状态和错误代码：http://msdn.microsoft.com/en-us/library/dd179382.aspx
    ///
    /// http://blogs.msdn.com/b/windowsazurestorage/archive/tags/scalability/
    /// http://blogs.msdn.com/b/windowsazurestorage/archive/2010/12/30/windows-azure-storage-architecture-overview.aspx
    /// http://blogs.msdn.com/b/windowsazurestorage/archive/2010/11/06/how-to-get-most-out-of-windows-azure-tables.aspx
    ///
    /// </summary>
    internal static class AzureQueueDefaultPolicies
    {
        public static int MaxQueueOperationRetries;
        public static TimeSpan PauseBetweenQueueOperationRetries;
        public static TimeSpan QueueOperationTimeout;

        static AzureQueueDefaultPolicies()
        {
            MaxQueueOperationRetries = 5;
            PauseBetweenQueueOperationRetries = TimeSpan.FromMilliseconds(100);
            QueueOperationTimeout = PauseBetweenQueueOperationRetries.Multiply(MaxQueueOperationRetries).Multiply(6);    // 3 秒。
        }
    }

    /// <summary>
    /// 用于封装对 Azure Queue Storage 访问的实用类。
    /// </summary>
    /// <remarks>
    /// 由 Azure Queue 流提供程序使用。
    /// </remarks>
    public partial class AzureQueueDataManager : IAzureQueueDataManager
    {
        /// <summary>此实例所管理的队列名称。</summary>
        public string QueueName { get; private set; }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        private readonly ILogger logger;
        private readonly TimeSpan? messageVisibilityTimeout;
        private readonly AzureQueueOptions options;
        private QueueClient? _queueClient;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="queueName">要连接的队列名称。</param>
        /// <param name="storageConnectionString">用于承载此表的 Azure Storage 账户连接字符串。</param>
        /// <param name="visibilityTimeout">指定可见性超时间隔的 TimeSpan。</param>
        public AzureQueueDataManager(ILoggerFactory loggerFactory, string queueName, string storageConnectionString, TimeSpan? visibilityTimeout = null)
            : this(loggerFactory, queueName, ConfigureOptions(storageConnectionString, visibilityTimeout))
        {
        }

        private static AzureQueueOptions ConfigureOptions(string storageConnectionString, TimeSpan? visibilityTimeout)
        {
            var options = new AzureQueueOptions
            {
                MessageVisibilityTimeout = visibilityTimeout
            };
            options.QueueServiceClient = new(storageConnectionString);
            return options;
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="queueName">要连接的队列名称。</param>
        /// <param name="options">队列连接选项。</param>
        /// <exception cref="ArgumentException">经过清理的 <paramref name="queueName"/> 违反 Azure Queue 命名规则。</exception>
        public AzureQueueDataManager(ILoggerFactory loggerFactory, string queueName, AzureQueueOptions options)
        {
            ArgumentNullException.ThrowIfNull(queueName);
            ArgumentNullException.ThrowIfNull(options);

            queueName = SanitizeQueueName(queueName);
            ValidateQueueName(queueName);

            logger = loggerFactory.CreateLogger<AzureQueueDataManager>();
            QueueName = queueName;
            messageVisibilityTimeout = options.MessageVisibilityTimeout;
            this.options = options;
        }

        private ValueTask<QueueClient> GetQueueClient()
        {
            if (_queueClient is { } client)
            {
                return new(client);
            }

            return GetQueueClientAsync();
            async ValueTask<QueueClient> GetQueueClientAsync() => _queueClient ??= await GetCloudQueueClient(options);
        }

        /// <summary>
        /// 初始化与队列的连接。
        /// </summary>
        public async Task InitQueueAsync()
        {
            var startTime = DateTime.UtcNow;

            try
            {
                // 如果队列尚不存在，则创建它。
                var client = await GetQueueClient();
                var response = await client.CreateIfNotExistsAsync();
                LogInfoAzureQueueConnection(QueueName);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "CreateIfNotExist", AzureQueueErrorCode.AzureQueue_02);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "InitQueue_Async");
            }
        }

        /// <summary>
        /// 删除队列。
        /// </summary>
        public async Task DeleteQueue()
        {
            var startTime = DateTime.UtcNow;
            LogTraceDeletingQueue(QueueName);
            try
            {
                // 这样就不必先创建队列，之后才能删除它。
                var client = await GetQueueClient();
                if (await client.DeleteIfExistsAsync())
                {
                    LogInfoAzureQueueDeleted(QueueName);
                }
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "DeleteQueue", AzureQueueErrorCode.AzureQueue_04);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "DeleteQueue");
            }
        }

        /// <summary>
        /// 清空队列。
        /// </summary>
        public async Task ClearQueue()
        {
            var startTime = DateTime.UtcNow;
            LogTraceClearingAQueue(QueueName);
            try
            {
                // 这样就不必先创建队列，之后才能删除它。
                var client = await GetQueueClient();
                await client.ClearMessagesAsync();
                LogInfoAzureQueueClear(QueueName);
            }
            catch (RequestFailedException exc)
            {
                if (exc.Status != (int)HttpStatusCode.NotFound)
                {
                    ReportErrorAndRethrow(exc, "ClearQueue", AzureQueueErrorCode.AzureQueue_06);
                }
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "ClearQueue", AzureQueueErrorCode.AzureQueue_06);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "ClearQueue");
            }
        }

        /// <summary>
        /// 将新消息添加到队列。
        /// </summary>
        /// <param name="message">要添加到队列的消息。</param>
        public async Task AddQueueMessage(string message)
        {
            var startTime = DateTime.UtcNow;
            LogTraceAddingMessage(message, QueueName);
            try
            {
                var client = await GetQueueClient();
                await client.SendMessageAsync(message);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "AddQueueMessage", AzureQueueErrorCode.AzureQueue_07);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "AddQueueMessage");
            }
        }

        /// <summary>
        /// 查看队列中的最新消息，而不将其出队。
        /// </summary>
        public async Task<PeekedMessage?> PeekQueueMessage()
        {
            var startTime = DateTime.UtcNow;
            LogTracePeekingMessage(QueueName);
            try
            {
                var client = await GetQueueClient();
                var messages = await client.PeekMessagesAsync(maxMessages: 1);
                return messages.Value.FirstOrDefault();

            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "PeekQueueMessage", AzureQueueErrorCode.AzureQueue_08);
                return null; // 使编译器接受此代码的占位语句。
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "PeekQueueMessage");
            }
        }


        /// <summary>
        /// 从队列获取一条新消息。
        /// </summary>
        public async Task<QueueMessage?> GetQueueMessage()
        {
            var startTime = DateTime.UtcNow;
            LogTraceGettingMessage(QueueName);
            try
            {
                // netstandard 不支持 BeginGetMessage 和 EndGetMessage，可以改用 GetMessageAsync。
                // http://msdn.microsoft.com/en-us/library/ee758456.aspx
                // 如果队列中没有可见消息，GetMessage 返回 null。
                var client = await GetQueueClient();
                var messages = await client.ReceiveMessagesAsync(maxMessages: 1, messageVisibilityTimeout);
                return messages.Value.FirstOrDefault();
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "GetQueueMessage", AzureQueueErrorCode.AzureQueue_09);
                return null; // 使编译器接受此代码的占位语句。
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "GetQueueMessage");
            }
        }

        /// <summary>
        /// 从队列获取若干条新消息。
        /// </summary>
        /// <param name="count">要从队列获取的消息数量。</param>
        public async Task<IEnumerable<QueueMessage>> GetQueueMessages(int? count = null)
        {
            var startTime = DateTime.UtcNow;
            if (count == -1)
            {
                count = null;
            }

            LogTraceGettingUpToMessages(count, QueueName);
            try
            {
                var client = await GetQueueClient();
                var messages = await client.ReceiveMessagesAsync(count, messageVisibilityTimeout);
                return messages.Value;
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "GetQueueMessages", AzureQueueErrorCode.AzureQueue_10);
                throw new InvalidOperationException("Unable to retrieve messages from the queue.");
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "GetQueueMessages");
            }
        }

        /// <summary>
        /// 从队列删除消息。
        /// </summary>
        /// <param name="message">要从队列删除的消息。</param>
        public async Task DeleteQueueMessage(QueueMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            var startTime = DateTime.UtcNow;
            LogTraceDeletingAMessage(QueueName);
            try
            {
                var client = await GetQueueClient();
                await client.DeleteMessageAsync(message.MessageId, message.PopReceipt);

            }
            catch (RequestFailedException exc)
            {
                if (exc.Status != (int)HttpStatusCode.NotFound)
                {
                    ReportErrorAndRethrow(exc, "DeleteMessage", AzureQueueErrorCode.AzureQueue_11);
                }
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "DeleteMessage", AzureQueueErrorCode.AzureQueue_11);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "DeleteQueueMessage");
            }
        }

        internal async Task ReleaseQueueMessage(QueueMessage message, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(message);

            var startTime = DateTime.UtcNow;
            try
            {
                var client = await GetQueueClient();
                await client.UpdateMessageAsync(
                    message.MessageId,
                    message.PopReceipt,
                    message.Body,
                    TimeSpan.Zero,
                    cancellationToken);
            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "ReleaseQueueMessage", AzureQueueErrorCode.AzureQueue_16);
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "ReleaseQueueMessage");
            }
        }

        Task IAzureQueueDataManager.ReleaseQueueMessage(QueueMessage message, CancellationToken cancellationToken) =>
            ReleaseQueueMessage(message, cancellationToken);

        internal async Task GetAndDeleteQueueMessage()
        {
            var message = await GetQueueMessage();
            await DeleteQueueMessage(message!);
        }

        /// <summary>
        /// 返回队列中的近似消息数量。
        /// </summary>
        public async Task<int> GetApproximateMessageCount()
        {
            var startTime = DateTime.UtcNow;
            LogTraceGetApproximateMessageCount(QueueName);
            try
            {
                var client = await GetQueueClient();
                var properties = await client.GetPropertiesAsync();
                return properties.Value.ApproximateMessagesCount;

            }
            catch (Exception exc)
            {
                ReportErrorAndRethrow(exc, "FetchAttributes", AzureQueueErrorCode.AzureQueue_12);
                return 0; // 使编译器接受此代码的占位语句。
            }
            finally
            {
                CheckAlertSlowAccess(startTime, "GetApproximateMessageCount");
            }
        }

        private void CheckAlertSlowAccess(DateTime startOperation, string operation)
        {
            var timeSpan = DateTime.UtcNow - startOperation;
            if (timeSpan > AzureQueueDefaultPolicies.QueueOperationTimeout)
            {
                LogWarningSlowAzureQueueAccess(QueueName, operation, timeSpan);
            }
        }

        private void ReportErrorAndRethrow(Exception exc, string operation, AzureQueueErrorCode errorCode)
        {
            var errMsg = string.Format(
                "Error doing {0} for Azure storage queue {1} " + Environment.NewLine
                + "Exception = {2}", operation, QueueName, exc);
            logger.LogError((int)errorCode, exc, "{Message}", errMsg); // TODO：等待 https://github.com/dotnet/runtime/issues/110570
            throw new AggregateException(errMsg, exc);
        }

        private async Task<QueueClient> GetCloudQueueClient(AzureQueueOptions options)
        {
            try
            {
                var client = await options.CreateClient!();
                return client.GetQueueClient(QueueName);
            }
            catch (Exception exc)
            {
                LogErrorCreatingAzureQueueClient(exc);
                throw;
            }
        }

        private static string SanitizeQueueName(string queueName)
        {
            var tmp = queueName;
            // Azure Queue 命名规则：https://learn.microsoft.com/rest/api/storageservices/Naming-Queues-and-Metadata?redirectedfrom=MSDN
#pragma warning disable CA1308 // Azure Queue 名称使用小写字母。
            tmp = tmp.ToLowerInvariant();
#pragma warning restore CA1308
            tmp = tmp
                .Replace('/', '-') // 正斜杠。
                .Replace('\\', '-') // 反斜杠。
                .Replace('#', '-') // 井号。
                .Replace('?', '-') // 问号。
                .Replace('&', '-')
                .Replace('+', '-')
                .Replace(':', '-')
                .Replace('.', '-')
                .Replace('%', '-');
            return tmp;
        }

        private static void ValidateQueueName(string queueName)
        {
            // 队列与元数据命名：http://msdn.microsoft.com/en-us/library/windowsazure/dd179349.aspx
            if (!(queueName.Length >= 3 && queueName.Length <= 63))
            {
                // 队列名称长度必须为 3 至 63 个字符。
                throw new ArgumentException(string.Format("A queue name must be from 3 through 63 characters long, while your queueName length is {0}, queueName is {1}.", queueName.Length, queueName), nameof(queueName));
            }

            if (!char.IsAsciiLetterOrDigit(queueName.First()))
            {
                // 队列名称必须以字母或数字开头。
                throw new ArgumentException(string.Format("A queue name must start with a letter or number, while your queueName is {0}.", queueName), nameof(queueName));
            }

            if (!char.IsAsciiLetterOrDigit(queueName.Last()))
            {
                // 队列名称的首尾字符必须为字母或数字，连字符 (-) 不能作为首尾字符。
                throw new ArgumentException(string.Format("The last letter in the queue name must be alphanumeric, while your queueName is {0}.", queueName), nameof(queueName));
            }

            if (!queueName.All(c => char.IsAsciiLetterOrDigit(c) || c.Equals('-')))
            {
                // 队列名称只能包含字母、数字和连字符 (-)。
                throw new ArgumentException(string.Format("A queue name can only contain ASCII letters, numbers, and the dash (-) character, while your queueName is {0}.", queueName), nameof(queueName));
            }

            if (queueName.Contains("--", StringComparison.Ordinal))
            {
                // 队列名称中不允许出现连续的连字符。
                throw new ArgumentException(string.Format("Consecutive dash characters are not permitted in the queue name, while your queueName is {0}.", queueName), nameof(queueName));
            }
        }

        [LoggerMessage(
            EventId = (int)AzureQueueErrorCode.AzureQueue_01,
            Level = LogLevel.Information,
            Message = "Connected to Azure storage queue {QueueName}"
        )]
        private partial void LogInfoAzureQueueConnection(string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Deleting queue: {QueueName}"
        )]
        private partial void LogTraceDeletingQueue(string queueName);

        [LoggerMessage(
            EventId = (int)AzureQueueErrorCode.AzureQueue_03,
            Level = LogLevel.Information,
            Message = "Deleted Azure Queue {QueueName}"
        )]
        private partial void LogInfoAzureQueueDeleted(string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Clearing a queue: {QueueName}"
        )]
        private partial void LogTraceClearingAQueue(string queueName);

        [LoggerMessage(
            EventId = (int)AzureQueueErrorCode.AzureQueue_05,
            Level = LogLevel.Information,
            Message = "Cleared Azure Queue {QueueName}"
        )]
        private partial void LogInfoAzureQueueClear(string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Adding message {Data} to queue: {QueueName}"
        )]
        private partial void LogTraceAddingMessage(string data, string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Peeking a message from queue: {QueueName}"
        )]
        private partial void LogTracePeekingMessage(string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Getting a message from queue: {QueueName}"
        )]
        private partial void LogTraceGettingMessage(string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Getting up to {MessageCount} messages from queue: {QueueName}"
        )]
        private partial void LogTraceGettingUpToMessages(int? messageCount, string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "Deleting a message from queue: {QueueName}"
        )]
        private partial void LogTraceDeletingAMessage(string queueName);

        [LoggerMessage(
            Level = LogLevel.Trace,
            Message = "GetApproximateMessageCount a message from queue: {QueueName}"
        )]
        private partial void LogTraceGetApproximateMessageCount(string queueName);

        [LoggerMessage(
            EventId = (int)AzureQueueErrorCode.AzureQueue_13,
            Level = LogLevel.Warning,
            Message = "Slow access to Azure queue {QueueName} for {Operation}, which took {TimeSpan}."
        )]
        private partial void LogWarningSlowAzureQueueAccess(string queueName, string operation, TimeSpan timeSpan);

        [LoggerMessage(
            EventId = (int)AzureQueueErrorCode.AzureQueue_14,
            Level = LogLevel.Error,
            Message = "Error creating Azure Storage Queues client"
        )]
        private partial void LogErrorCreatingAzureQueueClient(Exception exception);
    }
}
