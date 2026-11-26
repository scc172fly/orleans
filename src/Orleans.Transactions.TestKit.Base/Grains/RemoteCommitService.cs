using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义远程承诺用于测试交易承诺参与的结果。
    /// </summary>
    public interface IRemoteCommitService
    {
        /// <summary>
        /// 记录成功远程承诺。
        /// </summary>
        /// <param name="transactionId">承诺的交易识别符。</param>
        /// <param name="data">运行数据。</param>
        /// <returns><see langword="true"/> 汇报成功承诺。</returns>
        Task<bool> Pass(Guid transactionId, string data);

        /// <summary>
        /// 记录被否决的远程实施 。
        /// </summary>
        /// <param name="transactionId">被拒绝的交易识别符 。</param>
        /// <param name="data">运行数据。</param>
        /// <returns><see langword="false"/> 以报告未执行承诺。</returns>
        Task<bool> Fail(Guid transactionId, string data);

        /// <summary>
        /// 记录远程实施,然后宣布例外。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="data">运行数据。</param>
        /// <returns>总是有故障的任务。</returns>
        Task<bool> Throw(Guid transactionId, string data);
    }

    // TODO:替换为更完整的服务实施,即:
    // - 能够被传唤核查是否提供有适当参数的Callme服务。
    // 会造成错误的预言
    /// <summary>
    /// 提供确定性的成功、失败和错误的远程承诺结果,供交易测试使用。
    /// </summary>
    public partial class RemoteCommitService : IRemoteCommitService
    {
        private readonly ILogger logger;

        /// <summary>
        /// 初始化 <see cref="RemoteCommitService"/> 类的新实例。
        /// </summary>
        /// <param name="logger">日志器。</param>
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
    /// 代表一个远程承诺操作,它报告成功。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class PassOperation : ITransactionCommitOperation<IRemoteCommitService>
    {
        /// <summary>
        /// 获取或设置远程发送服务的数据。
        /// </summary>
        [Id(0)]
        public string Data { get; set; }

        /// <summary>
        /// 初始化 <see cref="PassOperation"/> 类的新实例。
        /// </summary>
        /// <param name="data">远程发送服务的数据 。</param>
        public PassOperation(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// 启动成功的远程任务执行 。
        /// </summary>
        /// <param name="transactionId">承诺的交易识别符。</param>
        /// <param name="service">远程送件服务。</param>
        /// <returns><see langword="true"/> 当远方的人成功的时候,</returns>
        public async Task<bool> Commit(Guid transactionId, IRemoteCommitService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            return await service.Pass(transactionId, this.Data);
        }
    }

    /// <summary>
    /// 代表一个远程承付操作,报告失败。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class FailOperation : ITransactionCommitOperation<IRemoteCommitService>
    {
        /// <summary>
        /// 获取或设置远程发送服务的数据。
        /// </summary>
        [Id(0)]
        public string Data { get; set; }

        /// <summary>
        /// 初始化 <see cref="FailOperation"/> 类的新实例。
        /// </summary>
        /// <param name="data">远程发送服务的数据 。</param>
        public FailOperation(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// 启动失败的远程执行操作 。
        /// </summary>
        /// <param name="transactionId">被拒绝的交易识别符 。</param>
        /// <param name="service">远程送件服务。</param>
        /// <returns><see langword="false"/> 拒绝远程承诺。</returns>
        public async Task<bool> Commit(Guid transactionId, IRemoteCommitService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            return await service.Fail(transactionId, this.Data);
        }
    }

    /// <summary>
    /// 代表一个远程实施操作, 并排除此例外 。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class ThrowOperation : ITransactionCommitOperation<IRemoteCommitService>
    {
        /// <summary>
        /// 获取或设置远程发送服务的数据。
        /// </summary>
        [Id(0)]
        public string Data { get; set; }

        /// <summary>
        /// 初始化 <see cref="ThrowOperation"/> 类的新实例。
        /// </summary>
        /// <param name="data">远程发送服务的数据 。</param>
        public ThrowOperation(string data)
        {
            this.Data = data;
        }

        /// <summary>
        /// 启动错误远程操作 。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="service">远程送件服务。</param>
        /// <returns>总是有故障的任务。</returns>
        public async Task<bool> Commit(Guid transactionId, IRemoteCommitService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            return await service.Throw(transactionId, this.Data);
        }
    }
}
