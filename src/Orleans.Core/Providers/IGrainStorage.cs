using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using System.Net;

namespace Orleans.Storage
{
    /// <summary>
    /// 能够读写 Orleans Grain 状态数据的存储提供程序应实现的接口。
    /// </summary>
    public interface IGrainStorage
    {
        /// <summary>此存储实例的读取数据操作。</summary>
        /// <param name="stateName">此 Grain 的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">要填充此 Grain 状态数据的对象。</param>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <returns>表示指定 Grain 的状态读取操作完成情况的异步任务。</returns>
        Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState);

        /// <summary>此存储实例的读取数据操作。</summary>
        /// <param name="stateName">此 Grain 的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">要填充此 Grain 状态数据的对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <returns>表示指定 Grain 的状态读取操作完成情况的异步任务。</returns>
        /// <remarks>默认实现委托给不带取消令牌的重载方法。</remarks>
        Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
            => ReadStateAsync(stateName, grainId, grainState);

        /// <summary>此存储实例的写入数据操作。</summary>
        /// <param name="stateName">此 Grain 的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">要为此 Grain 写入的状态数据对象。</param>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <returns>表示指定 Grain 的状态写入操作完成情况的异步任务。</returns>
        Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState);

        /// <summary>此存储实例的写入数据操作。</summary>
        /// <param name="stateName">此 Grain 的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">要为此 Grain 写入的状态数据对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <returns>表示指定 Grain 的状态写入操作完成情况的异步任务。</returns>
        /// <remarks>默认实现委托给不带取消令牌的重载方法。</remarks>
        Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
            => WriteStateAsync(stateName, grainId, grainState);

        /// <summary>此存储实例的删除或清除数据操作。</summary>
        /// <param name="stateName">此 Grain 的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">此 Grain 最后已知状态数据对象的副本。</param>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <returns>表示指定 Grain 的状态清除操作完成情况的异步任务。</returns>
        Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState);

        /// <summary>此存储实例的删除或清除数据操作。</summary>
        /// <param name="stateName">此 Grain 的状态名称。</param>
        /// <param name="grainId">Grain ID。</param>
        /// <param name="grainState">此 Grain 最后已知状态数据对象的副本。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <typeparam name="T">Grain 状态类型。</typeparam>
        /// <returns>表示指定 Grain 的状态清除操作完成情况的异步任务。</returns>
        /// <remarks>默认实现委托给不带取消令牌的重载方法。</remarks>
        Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
            => ClearStateAsync(stateName, grainId, grainState);
    }

    /// <summary>
    /// 存储提供程序可以选择实现的接口，用于返回更丰富的异常详细信息。
    /// TODO: 移除此接口。为满足监控需求，改用装饰器模式。 - jbragg
    /// </summary>
    public interface IRestExceptionDecoder
    {
        /// <summary>
        /// 解码异常详细信息。
        /// </summary>
        /// <param name="exception">要解码的异常。</param>
        /// <param name="httpStatusCode">错误对应的 HTTP 状态码。</param>
        /// <param name="restStatus">错误对应的 REST 状态。</param>
        /// <param name="getExtendedErrors">是否提取 REST 错误码。</param>
        /// <returns>指示异常是否已解码的值。</returns>
        bool DecodeException(Exception exception, out HttpStatusCode httpStatusCode, out string restStatus, bool getExtendedErrors = false);
    }

    /// <summary>
    /// 存储提供程序尝试执行 WriteStateAsync 操作时检测到 ETag 不一致所抛出的异常。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class InconsistentStateException : OrleansException
    {
        /// <summary>
        /// 获取或设置一个值，指示此异常是否发生于当前激活实例。
        /// </summary>
        [Id(0)]
        internal bool IsSourceActivation { get; set; } = true;

        /// <summary>获取当前保存在持久化存储中的 ETag 值。</summary>
        [Id(1)]
        public string? StoredEtag { get; private set; }

        /// <summary>获取当前保存在内存中并尝试更新的 ETag 值。</summary>
        [Id(2)]
        public string? CurrentEtag { get; private set; }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        public InconsistentStateException()
        {
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        public InconsistentStateException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="message">消息。</param>
        /// <param name="innerException">内部异常。</param>
        public InconsistentStateException(string message, Exception? innerException)
            : base(message, innerException!)
        {
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列化信息。</param>
        /// <param name="context">上下文。</param>
        [Obsolete]
        protected InconsistentStateException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            this.StoredEtag = info.GetString(nameof(StoredEtag));
            this.CurrentEtag = info.GetString(nameof(CurrentEtag));
            this.IsSourceActivation = info.GetBoolean(nameof(this.IsSourceActivation));
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="errorMsg">错误消息。</param>
        /// <param name="storedEtag">已存储的 ETag。</param>
        /// <param name="currentEtag">当前 ETag。</param>
        /// <param name="storageException">内部异常。</param>
        public InconsistentStateException(
          string errorMsg,
          string? storedEtag,
          string? currentEtag,
          Exception? storageException) : base(errorMsg, storageException!)
        {
            StoredEtag = storedEtag;
            CurrentEtag = currentEtag;
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="errorMsg">错误消息。</param>
        /// <param name="storedEtag">已存储的 ETag。</param>
        /// <param name="currentEtag">当前 ETag。</param>
        public InconsistentStateException(
          string errorMsg,
          string? storedEtag,
          string? currentEtag)
            : this(errorMsg, storedEtag, currentEtag, null)
        {
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="storedEtag">已存储的 ETag。</param>
        /// <param name="currentEtag">当前 ETag。</param>
        /// <param name="storageException">存储异常。</param>
        public InconsistentStateException(string? storedEtag, string? currentEtag, Exception storageException)
            : this((storageException ?? throw new ArgumentNullException(nameof(storageException))).Message, storedEtag, currentEtag, storageException)
        {
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("InconsistentStateException: {0} Expected Etag={1} Received Etag={2} {3}",
                Message, StoredEtag, CurrentEtag, InnerException);
        }

        /// <inheritdoc/>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            info.AddValue(nameof(StoredEtag), this.StoredEtag);
            info.AddValue(nameof(CurrentEtag), this.CurrentEtag);
            info.AddValue(nameof(this.IsSourceActivation), this.IsSourceActivation);
            base.GetObjectData(info, context);
        }
    }
}
