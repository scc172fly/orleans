using System;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Runtime;
using System.Net;

namespace Orleans.Storage
{
    /// <summary>
    /// 用于存储能够读写 Orleans grain 状态数据的存储,将实施接口。
    /// </summary>
    public interface IGrainStorage
    {
        /// <summary>读取此存储实例的数据函数。</summary>
        /// <param name="stateName">国家名称 grain</param>
        /// <param name="grainId">Grain ID 身份</param>
        /// <param name="grainState">为此要输入的州数据对象 grain.</param>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <returns>特定 grain的读化操作的完成承诺。</returns>
        Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState);

        /// <summary>读取此存储实例的数据函数。</summary>
        /// <param name="stateName">国家名称 grain.</param>
        /// <param name="grainId">Grain ID.</param>
        /// <param name="grainState">为此要输入的州数据对象 grain.</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <returns>特定 grain的读化操作的完成承诺。</returns>
        /// <remarks>默认执行代表将无取消符号的超载代表。</remarks>
        Task ReadStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
            => ReadStateAsync(stateName, grainId, grainState);

        /// <summary>为此存储实例写入数据函数 。</summary>
        /// <param name="stateName">国家名称 grain</param>
        /// <param name="grainId">Grain ID 身份</param>
        /// <param name="grainState">为此要写入的状态数据对象 grain.</param>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <returns>指定写入操作的完成承诺 grain.</returns>
        Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState);

        /// <summary>为此存储实例写入数据函数 。</summary>
        /// <param name="stateName">国家名称 grain.</param>
        /// <param name="grainId">Grain ID.</param>
        /// <param name="grainState">为此要写入的状态数据对象 grain.</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <returns>指定写入操作的完成承诺 grain.</returns>
        /// <remarks>默认执行代表将无取消符号的超载代表。</remarks>
        Task WriteStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
            => WriteStateAsync(stateName, grainId, grainState);

        /// <summary>删除/清除此存储实例的数据函数。</summary>
        /// <param name="stateName">国家名称 grain</param>
        /// <param name="grainId">Grain ID 身份</param>
        /// <param name="grainState">本 grain的最后已知的状态数据对象副本。</param>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <returns>特定 grain的删除操作的完成承诺。</returns>
        Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState);

        /// <summary>删除/清除此存储实例的数据函数。</summary>
        /// <param name="stateName">国家名称 grain.</param>
        /// <param name="grainId">Grain ID.</param>
        /// <param name="grainState">本 grain的最后已知的状态数据对象副本。</param>
        /// <param name="cancellationToken">取消牌。</param>
        /// <typeparam name="T"> grain 状态类型。</typeparam>
        /// <returns>特定 grain的删除操作的完成承诺。</returns>
        /// <remarks>默认执行代表将无取消符号的超载代表。</remarks>
        Task ClearStateAsync<T>(string stateName, GrainId grainId, IGrainState<T> grainState, CancellationToken cancellationToken)
            => ClearStateAsync(stateName, grainId, grainState);
    }

    /// <summary>
    /// 可通过存储来选择接口, 以返回更丰富的例外细节 。
    /// TODO: 删除此接口。 移动到装饰模式, 用于监测 。 - jbragg
    /// </summary>
    public interface IRestExceptionDecoder
    {
        /// <summary>
        /// 破解例外的细节
        /// </summary>
        /// <param name="exception">解码例外。</param>
        /// <param name="httpStatusCode">HTTP 状态代码错误。</param>
        /// <param name="restStatus">错误的 REST 状态。</param>
        /// <param name="getExtendedErrors">是否要提取 REST 错误代码。</param>
        /// <returns>表示该例外是否被解码的价值。</returns>
        bool DecodeException(Exception exception, out HttpStatusCode httpStatusCode, out string restStatus, bool getExtendedErrors = false);
    }

    /// <summary>
    /// 当储存在试图进行 WriteStateAsync 操作时发现气管不一致时,就投放例外。
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class InconsistentStateException : OrleansException
    {
        /// <summary>
        /// 获取或设定值,以显示当前激活时是否出现此例外。
        /// </summary>
        [Id(0)]
        internal bool IsSourceActivation { get; set; } = true;

        /// <summary>获取当前持续存储中的静态值 。</summary>
        [Id(1)]
        public string? StoredEtag { get; private set; }

        /// <summary>获取当前在记忆中帮助存储的静态值,并试图更新。</summary>
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
        /// <param name="message">电文。</param>
        public InconsistentStateException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="message">电文。</param>
        /// <param name="innerException">内在的例外</param>
        public InconsistentStateException(string message, Exception? innerException)
            : base(message, innerException!)
        {
        }

        /// <summary>
        /// 初始化 <see cref="InconsistentStateException"/> 类的新实例。
        /// </summary>
        /// <param name="info">序列号信息。</param>
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
        /// <param name="storedEtag">储存的ETag。</param>
        /// <param name="currentEtag">当前的ETag。</param>
        /// <param name="storageException">内在的例外</param>
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
        /// <param name="storedEtag">储存的ETag。</param>
        /// <param name="currentEtag">当前的ETag。</param>
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
        /// <param name="storedEtag">储存的ETag。</param>
        /// <param name="currentEtag">当前的ETag。</param>
        /// <param name="storageException">存储例外</param>
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
