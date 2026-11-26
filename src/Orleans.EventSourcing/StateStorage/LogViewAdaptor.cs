using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Orleans.EventSourcing.Common;

namespace Orleans.EventSourcing.StateStorage
{
    /// <summary>
    /// 环绕传统储存适配器并使用批量和电子标签的日志查看调整器
    /// 键附加条目。
    ///<para>
    /// 日志本身是短暂的, i.e.没有实际保存到存储中 - 只有最新的视图和一些
    /// 元数据(日志位置和写标记)被存储。
    /// </para>
    /// </summary>
    /// <typeparam name="TLogView">日志视图类型</typeparam>
    /// <typeparam name="TLogEntry">日志输入类型</typeparam>
    internal class LogViewAdaptor<TLogView, TLogEntry> : PrimaryBasedLogViewAdaptor<TLogView, TLogEntry, SubmissionEntry<TLogEntry>> where TLogView : class, new() where TLogEntry : class
    {
        /// <summary>
        /// 初始化 StorageProviderLogViewAdaptor 类
        /// </summary>
        public LogViewAdaptor(ILogViewAdaptorHost<TLogView, TLogEntry> host, TLogView initialState, IGrainStorage globalGrainStorage, string grainTypeName, ILogConsistencyProtocolServices services)
            : base(host, initialState, services)
        {
            this.globalGrainStorage = globalGrainStorage;
            this.grainTypeName = grainTypeName;
        }


        private const int maxEntriesInNotifications = 200;
        private readonly IGrainStorage globalGrainStorage;
        private readonly string grainTypeName;        // 包括元数据在内的已确认状态
        private GrainStateWithMetaDataAndETag<TLogView> GlobalStateCache = null!;

        /// <inheritdoc/>
        protected override TLogView LastConfirmedView()
        {
            return GlobalStateCache.StateAndMetaData.State;
        }

        /// <inheritdoc/>
        protected override int GetConfirmedVersion()
        {
            return GlobalStateCache.StateAndMetaData.GlobalVersion;
        }

        /// <inheritdoc/>
        protected override void InitializeConfirmedView(TLogView initialstate)
        {
            GlobalStateCache = new GrainStateWithMetaDataAndETag<TLogView>(initialstate);
        }

        protected override Task ClearPrimaryLogAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return this.globalGrainStorage.ClearStateAsync(grainTypeName, Services.GrainId, GlobalStateCache);
        }

        // 不需要特殊标签,因此我们创建了简单的提交条目
        /// <inheritdoc/>
        protected override SubmissionEntry<TLogEntry> MakeSubmissionEntry(TLogEntry entry)
        {
            return new SubmissionEntry<TLogEntry>() { Entry = entry };
        }

        /// <inheritdoc/>
        protected override async Task ReadAsync()
        {
            enter_operation("ReadAsync");

            while (true)
            {
                try
                {
                    // 供手工测试
                    //await Task.Delay(5000);

                    var readState = new GrainStateWithMetaDataAndETag<TLogView>();

                    await globalGrainStorage.ReadStateAsync(grainTypeName, Services.GrainId, readState);

                    GlobalStateCache = readState;

                    Services.Log(LogLevel.Debug, "read success {0}", GlobalStateCache);

                    LastPrimaryIssue.Resolve(Host, Services);

                    break; // 成功
                }
                catch (Exception e)
                {
                    LastPrimaryIssue.Record(new ReadFromStateStorageFailed() { Exception = e }, Host, Services);
                }

                Services.Log(LogLevel.Debug, "read failed {0}", LastPrimaryIssue);

                await LastPrimaryIssue.DelayBeforeRetry();
            }

            exit_operation("ReadAsync");
        }


        /// <inheritdoc/>
        protected override async Task<int> WriteAsync()
        {
            enter_operation("WriteAsync");

            var state = CopyTentativeState();
            var updates = GetCurrentBatchOfUpdates();
            bool batchsuccessfullywritten = false;

            var nextglobalstate = new GrainStateWithMetaDataAndETag<TLogView>(state);
            nextglobalstate.StateAndMetaData.WriteVector = GlobalStateCache.StateAndMetaData.WriteVector;
            nextglobalstate.StateAndMetaData.GlobalVersion = GlobalStateCache.StateAndMetaData.GlobalVersion + updates.Length;
            nextglobalstate.ETag = GlobalStateCache.ETag;

            var writebit = nextglobalstate.StateAndMetaData.FlipBit(Services.MyClusterId);

            try
            {
                // 供手工测试
                //await Task.Delay(5000);

                await globalGrainStorage.WriteStateAsync(grainTypeName, Services.GrainId, nextglobalstate);

                batchsuccessfullywritten = true;

                GlobalStateCache = nextglobalstate;

                Services.Log(LogLevel.Debug, "write ({0} updates) success {1}", updates.Length, GlobalStateCache);

                LastPrimaryIssue.Resolve(Host, Services);
            }
            catch (Exception e)
            {
                LastPrimaryIssue.Record(new UpdateStateStorageFailed() { Exception = e }, Host, Services);
            }

            if (!batchsuccessfullywritten)
            {
                Services.Log(LogLevel.Debug, "write apparently failed {0} {1}", nextglobalstate, LastPrimaryIssue);

                while (true) // 顽固到我们看得懂为止
                {

                    await LastPrimaryIssue.DelayBeforeRetry();

                    try
                    {
                        await globalGrainStorage.ReadStateAsync(grainTypeName, Services.GrainId, GlobalStateCache);

                        Services.Log(LogLevel.Debug, "read success {0}", GlobalStateCache);

                        LastPrimaryIssue.Resolve(Host, Services);

                        break;
                    }
                    catch (Exception e)
                    {
                        LastPrimaryIssue.Record(new ReadFromStateStorageFailed() { Exception = e }, Host, Services);
                    }

                    Services.Log(LogLevel.Debug, "read failed {0}", LastPrimaryIssue);
                }

                // 检查是否上次明显失败的写成功

                if (writebit == GlobalStateCache.StateAndMetaData.GetBit(Services.MyClusterId))
                {
                    Services.Log(LogLevel.Debug, "last write ({0} updates) was actually a success {1}", updates.Length, GlobalStateCache);

                    batchsuccessfullywritten = true;
                }
            }

            exit_operation("WriteAsync");

            if (!batchsuccessfullywritten)
                return 0;

            return updates.Length;
        }


        /// <summary>
        /// 描述更新主贮存时出现的连接问题。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class UpdateStateStorageFailed : PrimaryOperationFailed
        {
            /// <inheritdoc/>
            public override string ToString()
            {
                return $"write state to storage failed: caught {Exception.GetType().Name}: {Exception.Message}";
            }
        }


        /// <summary>
        /// 描述读取主贮存时出现的连接问题。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class ReadFromStateStorageFailed : PrimaryOperationFailed
        {
            /// <inheritdoc/>
            public override string ToString()
            {
                return $"read state from storage failed: caught {Exception.GetType().Name}: {Exception.Message}";
            }
        }


        /// <summary>
        /// 在更新存储中的 grain 后,向远程实例发送通知信息。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        protected internal sealed class UpdateNotificationMessage : INotificationMessage
        {
            /// <inheritdoc/>
            [Id(0)]
            public int Version { get; set; }

            /// <summary> 进行更新的组群 </summary>
            [Id(1)]
            public string Origin { get; set; } = null!;

            /// <summary> 适用更新的清单 </summary>
            [Id(2)]
            public List<TLogEntry> Updates { get; set; } = null!;

            /// <summary> 应用更新后存储的电子标签</summary>
            [Id(3)]
            public string? ETag { get; set; }

            /// <inheritdoc/>
            public override string ToString()
            {
                return string.Format("v{0} ({1} updates by {2}) etag={3}", Version, Updates.Count, Origin, ETag);
            }
        }

        /// <inheritdoc/>
        protected override INotificationMessage Merge(INotificationMessage earlierMessage, INotificationMessage laterMessage)
        {
            var earlier = earlierMessage as UpdateNotificationMessage;
            var later = laterMessage as UpdateNotificationMessage;

            if (earlier != null
                && later != null
                && earlier.Origin == later.Origin
                && earlier.Version + later.Updates.Count == later.Version
                && earlier.Updates.Count + later.Updates.Count < maxEntriesInNotifications)

                return new UpdateNotificationMessage()
                {
                    Version = later.Version,
                    Origin = later.Origin,
                    Updates = earlier.Updates.Concat(later.Updates).ToList(),
                    ETag = later.ETag
                };

            else
                return base.Merge(earlierMessage, laterMessage); // 只保留版本号
        }

        private readonly SortedList<long, UpdateNotificationMessage> notifications = new SortedList<long, UpdateNotificationMessage>();

        /// <inheritdoc/>
        protected override void OnNotificationReceived(INotificationMessage payload)
        {
            var um = payload as UpdateNotificationMessage;
            if (um != null)
                notifications.Add(um.Version - um.Updates.Count, um);
            else
                base.OnNotificationReceived(payload);
        }

        /// <inheritdoc/>
        protected override void ProcessNotifications()
        {
            // 已经确认的状态背后的废弃通知
            while (notifications.Count > 0 && notifications.ElementAt(0).Key < GlobalStateCache.StateAndMetaData.GlobalVersion)
            {
                Services.Log(LogLevel.Debug, "discarding notification {0}", notifications.ElementAt(0).Value);
                notifications.RemoveAt(0);
            }

            // 反映下一个全球版本的处理通知
            while (notifications.Count > 0 && notifications.ElementAt(0).Key == GlobalStateCache.StateAndMetaData.GlobalVersion)
            {
                var updateNotification = notifications.ElementAt(0).Value;
                notifications.RemoveAt(0);

                // 应用所有待处理操作
                foreach (var u in updateNotification.Updates)
                    try
                    {
                        Host.UpdateView(GlobalStateCache.StateAndMetaData.State, u);
                    }
                    catch (Exception e)
                    {
                        Services.CaughtUserCodeException("UpdateView", nameof(ProcessNotifications), e);
                    }

                GlobalStateCache.StateAndMetaData.GlobalVersion = updateNotification.Version;

                GlobalStateCache.StateAndMetaData.FlipBit(updateNotification.Origin);

                GlobalStateCache.ETag = updateNotification.ETag;

                Services.Log(LogLevel.Debug, "notification success ({0} updates) {1}", updateNotification.Updates.Count, GlobalStateCache);
            }

            Services.Log(LogLevel.Trace, "unprocessed notifications in queue: {0}", notifications.Count);

            base.ProcessNotifications();

        }


#if DEBUG
        private bool operation_in_progress;
#endif

        [Conditional("DEBUG")]
        private void enter_operation(string name)
        {
#if DEBUG
            Services.Log(LogLevel.Trace, "/-- enter {0}", name);
            Debug.Assert(!operation_in_progress);
            operation_in_progress = true;
#endif
        }

        [Conditional("DEBUG")]
        private void exit_operation(string name)
        {
#if DEBUG
            Services.Log(LogLevel.Trace, "\\-- exit {0}", name);
            Debug.Assert(operation_in_progress);
            operation_in_progress = false;
#endif
        }
    }
}
