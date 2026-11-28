using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义用于测试事务提交参与行为的远程提交结果。
    /// </summary>
    public interface IRemoteCommitService
    {
        /// <summary>
        /// 记录成功的远程提交。
        /// </summary>
        /// <param name="transactionId">已提交的事务标识符。</param>
        /// <param name="data">操作数据。</param>
        /// <returns><see langword="true"/> 表示提交成功。</returns>
        Task<bool> Pass(Guid transactionId, string data);

        /// <summary>
        /// 记录被拒绝的远程提交。
        /// </summary>
        /// <param name="transactionId">被拒绝的事务标识符。</param>
        /// <param name="data">操作数据。</param>
        /// <returns><see langword="false"/> 表示提交失败。</returns>
        Task<bool> Fail(Guid transactionId, string data);

        /// <summary>
        /// 记录远程提交，随后引发异常。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="data">操作数据。</param>
        /// <returns>始终进入故障状态的任务。</returns>
        Task<bool> Throw(Guid transactionId, string data);
    }

    // TODO：替换为更加完整的服务实现，该实现：
    // - 可以被调用以验证提交服务是否使用正确参数收到 Callme 调用。
    // - 可以针对故障场景产生错误。
    /// <summary>
    /// 为事务测试提供确定性的远程提交成功、失败及异常结果。
    /// </summary>
    public partial class RemoteCommitService : IRemoteCommitService
    {
        private readonly ILogger logger;

        /// <summary>
        /// 初始化 <see cref="RemoteCommitService"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        public RemoteCommitService(ILogger<RemoteCommitService> logger)
        {
            this.logger = logger;
        }

        /// <inheritdoc/>
        public async Task<bool> Pass(Guid transactionId, string data)
        {
            LogInformationTransactionPassed(this.logger, transactionId, data);
            await Task.Delay(30);
            return true;
        }

        /// <inheritdoc/>
        public async Task<bool> Fail(Guid transactionId, string data)
        {
            LogInformationTransactionFailed(this.logger, transactionId, data);
            await Task.Delay(30);
            return false;
        }

        /// <inheritdoc/>
        public async Task<bool> Throw(Guid transactionId, string data)
        {
            LogInformationTransactionThrew(this.logger, transactionId, data);
            await Task.Delay(30);
            throw new ApplicationException("Transaction {transactionId} Threw with data: {data}");
        }

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Transaction {TransactionId} Passed with data: {Data}"
        )]
        private static partial void LogInformationTransactionPassed(ILogger logger, Guid transactionId, string data);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Transaction {TransactionId} Failed with data: {Data}"
        )]
        private static partial void LogInformationTransactionFailed(ILogger logger, Guid transactionId, string data);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Transaction {TransactionId} Threw with data: {Data}"
        )]
        private static partial void LogInformationTransactionThrew(ILogger logger, Guid transactionId, string data);
    }

    /// <summary>
    /// 表示报告成功的远程提交操作。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class PassOperation : ITransactionCommitOperation<IRemoteCommitService>
    {
        /// <summary>
        /// 获取或设置提供给远程提交服务的数据。
        /// </summary>
        [Id(0)]
        public string Data { get; set; }

        /// <summary>
        /// 初始化 <see cref="PassOperation"/> 类的新实例。
        /// </summary>
        /// <param name="data">提供给远程提交服务的数据。</param>
        public PassOperation(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// 调用成功的远程提交操作。
        /// </summary>
        /// <param name="transactionId">已提交的事务标识符。</param>
        /// <param name="service">远程提交服务。</param>
        /// <returns>远程提交成功时为 <see langword="true"/>。</returns>
        public async Task<bool> Commit(Guid transactionId, IRemoteCommitService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            return await service.Pass(transactionId, this.Data);
        }
    }

    /// <summary>
    /// 表示报告失败的远程提交操作。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class FailOperation : ITransactionCommitOperation<IRemoteCommitService>
    {
        /// <summary>
        /// 获取或设置提供给远程提交服务的数据。
        /// </summary>
        [Id(0)]
        public string Data { get; set; }

        /// <summary>
        /// 初始化 <see cref="FailOperation"/> 类的新实例。
        /// </summary>
        /// <param name="data">提供给远程提交服务的数据。</param>
        public FailOperation(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// 调用失败的远程提交操作。
        /// </summary>
        /// <param name="transactionId">被拒绝的事务标识符。</param>
        /// <param name="service">远程提交服务。</param>
        /// <returns><see langword="false"/> 表示拒绝远程提交。</returns>
        public async Task<bool> Commit(Guid transactionId, IRemoteCommitService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            return await service.Fail(transactionId, this.Data);
        }
    }

    /// <summary>
    /// 表示引发异常的远程提交操作。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ThrowOperation : ITransactionCommitOperation<IRemoteCommitService>
    {
        /// <summary>
        /// 获取或设置提供给远程提交服务的数据。
        /// </summary>
        [Id(0)]
        public string Data { get; set; }

        /// <summary>
        /// 初始化 <see cref="ThrowOperation"/> 类的新实例。
        /// </summary>
        /// <param name="data">提供给远程提交服务的数据。</param>
        public ThrowOperation(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// 调用会引发异常的远程提交操作。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="service">远程提交服务。</param>
        /// <returns>始终进入故障状态的任务。</returns>
        public async Task<bool> Commit(Guid transactionId, IRemoteCommitService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            return await service.Throw(transactionId, this.Data);
        }
    }
}
