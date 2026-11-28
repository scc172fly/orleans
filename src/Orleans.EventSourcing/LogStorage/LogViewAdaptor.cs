using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Orleans.EventSourcing.Common;

namespace Orleans.EventSourcing.LogStorage
{
    /// <summary>
    /// 封装传统存储适配器，并通过批处理与 ETag
    /// 追加条目的日志视图适配器。
    ///<para>
    /// 日志本身是临时的，即实际不保存到存储中；只保存最新视图及部分
    /// 元数据（日志位置及写入标志）。（译注：当前实现实际将包含完整日志的 GlobalLog 读写到存储；上述描述与实现不符。）
    /// </para>
    /// </summary>
    /// <typeparam name="TLogView">日志视图的类型。</typeparam>
    /// <typeparam name="TLogEntry">日志条目的类型。</typeparam>
    internal class LogViewAdaptor<TLogView, TLogEntry> : PrimaryBasedLogViewAdaptor<TLogView, TLogEntry, SubmissionEntry<TLogEntry>> where TLogView : class, new() where TLogEntry : class
    {
        /// <summary>
        /// 初始化 StorageProviderLogViewAdaptor 类的实例。
        /// </summary>
        public LogViewAdaptor(ILogViewAdaptorHost<TLogView, TLogEntry> host, TLogView initialState, IGrainStorage globalGrainStorage, string grainTypeName, ILogConsistencyProtocolServices services)
            : base(host, initialState, services)
        {
            this.globalGrainStorage = globalGrainStorage;
            this.grainTypeName = grainTypeName;
        }


        private const int maxEntriesInNotifications = 200;
        private readonly IGrainStorage globalGrainStorage;
        private readonly string grainTypeName;

        // 包含整个日志的对象，与从存储读取或发送到存储的对象一致。
        private LogStateWithMetaDataAndETag<TLogEntry> GlobalLog = null!;

        // 已确认视图。
        private TLogView ConfirmedViewInternal = null!;
        private int ConfirmedVersionInternal;

        /// <inheritdoc/>
        protected override TLogView LastConfirmedView()
        {
            return ConfirmedViewInternal;
        }

        /// <inheritdoc/>
        protected override int GetConfirmedVersion()
        {
            return ConfirmedVersionInternal;
        }

        /// <inheritdoc/>
        protected override void InitializeConfirmedView(TLogView initialstate)
        {
            GlobalLog = new LogStateWithMetaDataAndETag<TLogEntry>();
            ConfirmedViewInternal = initialstate;
            ConfirmedVersionInternal = 0;
        }

        protected override Task ClearPrimaryLogAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return this.globalGrainStorage.ClearStateAsync(grainTypeName, Services.GrainId, GlobalLog);
        }

        private void UpdateConfirmedView()
        {
            for (int i = ConfirmedVersionInternal; i < GlobalLog.StateAndMetaData.Log.Count; i++)
            {
                try
                {
                    Host.UpdateView(ConfirmedViewInternal, GlobalLog.StateAndMetaData.Log[i]);
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("UpdateView", nameof(UpdateConfirmedView), e);
                }
            }
            ConfirmedVersionInternal = GlobalLog.StateAndMetaData.GlobalVersion;
        }


        /// <inheritdoc/>
        public override Task<IReadOnlyList<TLogEntry>> RetrieveLogSegment(int fromVersion, int toVersion)
        {

            // 复制所请求范围内的条目。
            IReadOnlyList<TLogEntry> segment = GlobalLog.StateAndMetaData.Log.GetRange(fromVersion, (toVersion - fromVersion));

            return Task.FromResult(segment);
        }

        // 不需要特殊标记，因此创建普通的提交条目。
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
                    // 用于手动测试。
                    //await Task.Delay(5000);

                    await globalGrainStorage.ReadStateAsync(grainTypeName, Services.GrainId, GlobalLog);

                    Services.Log(LogLevel.Debug, "read success {0}", GlobalLog);

                    UpdateConfirmedView();

                    LastPrimaryIssue.Resolve(Host, Services);

                    break; // 成功。
                }
                catch (Exception e)
                {
                    LastPrimaryIssue.Record(new ReadFromLogStorageFailed() { Exception = e }, Host, Services);
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

            var updates = GetCurrentBatchOfUpdates();
            bool batchsuccessfullywritten = false;

            var writebit = GlobalLog.StateAndMetaData.FlipBit(Services.MyClusterId);
            foreach (var x in updates)
                GlobalLog.StateAndMetaData.Log.Add(x.Entry!);

            try
            {
                // 用于手动测试。
                //await Task.Delay(5000);

                await globalGrainStorage.WriteStateAsync(grainTypeName, Services.GrainId, GlobalLog);

                batchsuccessfullywritten = true;

                Services.Log(LogLevel.Debug, "write ({0} updates) success {1}", updates.Length, GlobalLog);

                UpdateConfirmedView();

                LastPrimaryIssue.Resolve(Host, Services);
            }
            catch (Exception e)
            {
                LastPrimaryIssue.Record(new UpdateLogStorageFailed() { Exception = e }, Host, Services);
            }

            if (!batchsuccessfullywritten)
            {
                Services.Log(LogLevel.Debug, "write apparently failed {0}", LastPrimaryIssue);

                while (true) // 持续重试，直到能够读取存储中的内容。
                {

                    await LastPrimaryIssue.DelayBeforeRetry();

                    try
                    {
                        await globalGrainStorage.ReadStateAsync(grainTypeName, Services.GrainId, GlobalLog);

                        Services.Log(LogLevel.Debug, "read success {0}", GlobalLog);

                        UpdateConfirmedView();

                        LastPrimaryIssue.Resolve(Host, Services);

                        break;
                    }
                    catch (Exception e)
                    {
                        LastPrimaryIssue.Record(new ReadFromLogStorageFailed() { Exception = e }, Host, Services);
                    }

                    Services.Log(LogLevel.Debug, "read failed {0}", LastPrimaryIssue);
                }

                // 检查上一次看似失败的写入实际上是否已成功。

                if (writebit == GlobalLog.StateAndMetaData.GetBit(Services.MyClusterId))
                {
                    Services.Log(LogLevel.Debug, "last write ({0} updates) was actually a success {1}", updates.Length, GlobalLog);

                    batchsuccessfullywritten = true;
                }
            }

            exit_operation("WriteAsync");

            if (!batchsuccessfullywritten)
                return 0;

            return updates.Length;
        }


        /// <summary>
        /// 描述更新主存储时发生的连接问题。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class UpdateLogStorageFailed : PrimaryOperationFailed
        {
            /// <inheritdoc/>
            public override string ToString()
            {
                return $"write entire log to storage failed: caught {Exception.GetType().Name}: {Exception.Message}";
            }
        }


        /// <summary>
        /// 描述读取主存储时发生的连接问题。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class ReadFromLogStorageFailed : PrimaryOperationFailed
        {
            /// <inheritdoc/>
            public override string ToString()
            {
                return $"read entire log from storage failed: caught {Exception.GetType().Name}: {Exception.Message}";
            }
        }


        /// <summary>
        /// 在存储中更新此 Grain 后发送给远程实例的通知消息。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        protected internal sealed class UpdateNotificationMessage : INotificationMessage
        {
            /// <inheritdoc/>
            [Id(0)]
            public int Version { get; set; }

            /// <summary>执行更新的集群。</summary>
            [Id(1)]
            public string Origin { get; set; } = null!;

            /// <summary>已应用的更新列表。</summary>
            [Id(2)]
            public List<TLogEntry> Updates { get; set; } = null!;

            /// <summary>应用更新后存储的 ETag。</summary>
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
                return base.Merge(earlierMessage, laterMessage); // 仅保留版本号。
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
            // 丢弃落后于已确认状态的通知。
            while (notifications.Count > 0 && notifications.ElementAt(0).Key < GlobalLog.StateAndMetaData.GlobalVersion)
            {
                Services.Log(LogLevel.Debug, "discarding notification {0}", notifications.ElementAt(0).Value);
                notifications.RemoveAt(0);
            }

            // 处理反映下一个全局版本的通知。
            while (notifications.Count > 0 && notifications.ElementAt(0).Key == GlobalLog.StateAndMetaData.GlobalVersion)
            {
                var updateNotification = notifications.ElementAt(0).Value;
                notifications.RemoveAt(0);

                // 追加待处理的所有操作。
                foreach (var u in updateNotification.Updates)
                    GlobalLog.StateAndMetaData.Log.Add(u);

                GlobalLog.StateAndMetaData.FlipBit(updateNotification.Origin);

                GlobalLog.ETag = updateNotification.ETag;

                UpdateConfirmedView();

                Services.Log(LogLevel.Debug, "notification success ({0} updates) {1}", updateNotification.Updates.Count, GlobalLog);
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
