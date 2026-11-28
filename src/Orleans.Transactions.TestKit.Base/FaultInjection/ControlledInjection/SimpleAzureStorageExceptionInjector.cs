using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.Serialization;
using Azure;
using Microsoft.Extensions.Logging;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 在事务状态存储操作中注入受控的 Azure 存储异常或一般异常。
    /// </summary>
    public partial class SimpleAzureStorageExceptionInjector : IControlledTransactionFaultInjector, ITransactionScopedFaultInjector
    {
        private readonly object lockObj = new();
        private Guid? targetTransactionId;
        /// <inheritdoc />
        public bool InjectBeforeStore { get; set; }

        /// <inheritdoc />
        public bool InjectAfterStore { get; set; }

        /// <inheritdoc />
        public bool InjectGenericAfterStore { get; set; }
        private int injectionBeforeStoreCounter = 0;
        private int injectionAfterStoreCounter = 0;
        private int genericInjectionAfterStoreCounter = 0;
        private readonly ILogger logger;
        /// <summary>
        /// 初始化 <see cref="SimpleAzureStorageExceptionInjector"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        public SimpleAzureStorageExceptionInjector(ILogger<SimpleAzureStorageExceptionInjector> logger)
        {
            this.logger = logger;
        }

        /// <inheritdoc />
        public void AfterStore()
        {
            this.AfterStore(default);
        }

        void ITransactionScopedFaultInjector.Arm(
            Guid transactionId,
            FaultInjectionType injectionType,
            bool requireTransactionMatch)
        {
            lock (this.lockObj)
            {
                this.targetTransactionId = requireTransactionMatch ? transactionId : null;
                this.InjectBeforeStore = injectionType == FaultInjectionType.ExceptionBeforeStore;
                this.InjectAfterStore = injectionType == FaultInjectionType.ExceptionAfterStore;
                this.InjectGenericAfterStore = injectionType == FaultInjectionType.GenericExceptionAfterStore;
            }
        }

        void ITransactionScopedFaultInjector.BeforeStore(ImmutableArray<Guid> transactionIds)
            => this.BeforeStore(transactionIds);

        void ITransactionScopedFaultInjector.AfterStore(ImmutableArray<Guid> transactionIds)
            => this.AfterStore(transactionIds);

        private void AfterStore(ImmutableArray<Guid> transactionIds)
        {
            lock (this.lockObj)
            {
                if (!this.IsTargetStore(transactionIds))
                {
                    return;
                }

                if (this.InjectAfterStore)
                {
                    this.InjectAfterStore = false;
                    this.targetTransactionId = null;
                    this.injectionAfterStoreCounter++;
                    var message = $"Storage exception thrown after store, thrown total {injectionAfterStoreCounter}";
                    LogInformationMessage(this.logger, message);
                    throw new SimpleAzureStorageException(message);
                }

                if (this.InjectGenericAfterStore)
                {
                    this.InjectGenericAfterStore = false;
                    this.targetTransactionId = null;
                    this.genericInjectionAfterStoreCounter++;
                    var message = $"Generic storage exception thrown after store, thrown total {genericInjectionAfterStoreCounter}";
                    LogInformationMessage(this.logger, message);
                    throw new InvalidOperationException(message);
                }
            }
        }

        /// <inheritdoc />
        public void BeforeStore()
        {
            this.BeforeStore(default);
        }

        private void BeforeStore(ImmutableArray<Guid> transactionIds)
        {
            lock (this.lockObj)
            {
                if (!this.IsTargetStore(transactionIds) || !this.InjectBeforeStore)
                {
                    return;
                }

                this.InjectBeforeStore = false;
                this.targetTransactionId = null;
                this.injectionBeforeStoreCounter++;
                var message = $"Storage exception thrown before store. Thrown total {injectionBeforeStoreCounter}";
                LogInformationMessage(this.logger, message);
                throw new SimpleAzureStorageException(message);
            }
        }

        private bool IsTargetStore(ImmutableArray<Guid> transactionIds)
            => this.targetTransactionId is not { } targetTransactionId
                || (!transactionIds.IsDefaultOrEmpty && transactionIds.IndexOf(targetTransactionId) >= 0);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "{Message}"
        )]
        private static partial void LogInformationMessage(ILogger logger, string message);
    }

    /// <summary>
    /// 表示为事务测试注入的 Azure 存储异常。
    /// </summary>
    [GenerateSerializer]
    public class SimpleAzureStorageException : RequestFailedException
    {
        /// <summary>
        /// 初始化 <see cref="SimpleAzureStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="message">错误消息。</param>
        public SimpleAzureStorageException(string message) : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SimpleAzureStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="message">错误消息。</param>
        /// <param name="innerException">导致此异常的异常。</param>
        public SimpleAzureStorageException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SimpleAzureStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="status">响应的 HTTP 状态码。</param>
        /// <param name="message">错误消息。</param>
        public SimpleAzureStorageException(int status, string message) : base(status, message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SimpleAzureStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="status">响应的 HTTP 状态码。</param>
        /// <param name="message">错误消息。</param>
        /// <param name="innerException">导致此异常的异常。</param>
        public SimpleAzureStorageException(int status, string message, Exception innerException) : base(status, message, innerException)
        {
        }

        /// <summary>
        /// 初始化 <see cref="SimpleAzureStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="status">响应的 HTTP 状态码。</param>
        /// <param name="message">错误消息。</param>
        /// <param name="errorCode">特定于服务的错误代码。</param>
        /// <param name="innerException">导致此异常的异常。</param>
        public SimpleAzureStorageException(int status, string message, string errorCode, Exception innerException) : base(status, message, errorCode, innerException)
        {
        }

        /// <summary>
        /// 从序列化数据初始化 <see cref="SimpleAzureStorageException"/> 类的新实例。
        /// </summary>
        /// <param name="info">已序列化的异常数据。</param>
        /// <param name="context">有关序列化数据来源或目标的上下文。</param>
        [Obsolete("TThe serialization constructor pattern was made obsolete in modern versions of .NET. Use the other constructors instead.")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected SimpleAzureStorageException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
