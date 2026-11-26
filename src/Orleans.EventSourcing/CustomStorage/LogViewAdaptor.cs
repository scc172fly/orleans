using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Storage;
using Orleans.EventSourcing.Common;

namespace Orleans.EventSourcing.CustomStorage
{
    /// <summary>
    /// 使用用户提供的存储接口 <see cref="ICustomStorageInterface{T,E}"/>的日志一致性调整器。 
    /// 此接口必须由使用此日志视图适配器的任何 grain 来实施。
    /// </summary>
    /// <typeparam name="TLogView">日志视图类型</typeparam>
    /// <typeparam name="TLogEntry">日志输入类型</typeparam>
    internal class CustomStorageAdaptor<TLogView, TLogEntry> : PrimaryBasedLogViewAdaptor<TLogView, TLogEntry, SubmissionEntry<TLogEntry>>
        where TLogView : class, new()
        where TLogEntry : class
    {
        /// <summary>
        /// 初始化 CustomStorageAdaptor 类新实例
        /// </summary>
        public CustomStorageAdaptor(ILogViewAdaptorHost<TLogView, TLogEntry> host, TLogView initialState,
            ILogConsistencyProtocolServices services, string? primaryCluster)
            : base(host, initialState, services)
        {
            if (!(host is ICustomStorageInterface<TLogView, TLogEntry> customGrainStorage))
            {
                throw new BadProviderConfigException("Must implement ICustomStorageInterface<TLogView,TLogEntry> for CustomStorageLogView provider");
            }

            this.customGrainStorage = customGrainStorage;
            this.primaryCluster = primaryCluster;
        }

        private readonly string? primaryCluster;
        private readonly ICustomStorageInterface<TLogView, TLogEntry> customGrainStorage;

        private TLogView cached = null!;
        private int version;

        /// <inheritdoc/>
        protected override TLogView LastConfirmedView()
        {
            return cached;
        }

        /// <inheritdoc/>
        protected override int GetConfirmedVersion()
        {
            return version;
        }

        /// <inheritdoc/>
        protected override Task ClearPrimaryLogAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return this.customGrainStorage.ClearStoredState();
        }

        /// <inheritdoc/>
        protected override void InitializeConfirmedView(TLogView initialstate)
        {
            cached = initialstate;
            version = 0;
        }

        /// <inheritdoc/>
        protected override bool SupportSubmissions
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc/>
        protected override SubmissionEntry<TLogEntry> MakeSubmissionEntry(TLogEntry entry)
        {
            // 不需要特殊标签,因此我们创建了简单的提交条目
            return new SubmissionEntry<TLogEntry>() { Entry = entry };
        }

        [Serializable]
        [GenerateSerializer]
        internal sealed class ReadRequest : ILogConsistencyProtocolMessage
        {
            [Id(0)]
            public int KnownVersion { get; set; }
        }

        [Serializable]
        [GenerateSerializer]
        internal sealed class ReadResponse<ViewType> : ILogConsistencyProtocolMessage
        {
            [Id(0)]
            public int Version { get; set; }

            [Id(1)]
            public ViewType? Value { get; set; }
        }

        /// <inheritdoc/>
        protected override Task<ILogConsistencyProtocolMessage> OnMessageReceived(ILogConsistencyProtocolMessage payload)
        {
            var request = (ReadRequest)payload;

            var response = new ReadResponse<TLogView>() { Version = version };

            // 优化: 只包括版本更新后的值
            if (version > request.KnownVersion)
                response.Value = cached;

            return Task.FromResult<ILogConsistencyProtocolMessage>(response);
        }

        /// <inheritdoc/>
        protected override async Task ReadAsync()
        {
            enter_operation("ReadAsync");

            while (true)
            {
                try
                {
                    // 取自存储
                    var result = await ((ICustomStorageInterface<TLogView, TLogEntry>)Host).ReadStateFromStorage();
                    version = result.Key;
                    cached = result.Value;

                    Services.Log(LogLevel.Debug, "read success v{0}", version);

                    LastPrimaryIssue.Resolve(Host, Services);

                    break; // 成功
                }
                catch (Exception e)
                {
                    // 被转发的un wrap 内例外 - 有助于调试
                    if ((e as ProtocolTransportException)?.InnerException != null)
                        e = ((ProtocolTransportException)e).InnerException!;

                    LastPrimaryIssue.Record(new ReadFromPrimaryFailed() { Exception = e }, Host, Services);
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

            var updates = GetCurrentBatchOfUpdates().Select(submissionentry => submissionentry.Entry!).ToList();
            bool writesuccessful = false;
            bool transitionssuccessful = false;

            try
            {
                writesuccessful = await ((ICustomStorageInterface<TLogView, TLogEntry>)Host).ApplyUpdatesToStorage(updates, version);

                LastPrimaryIssue.Resolve(Host, Services);
            }
            catch (Exception e)
            {
                // 被转发的un wrap 内例外 - 有助于调试
                if ((e as ProtocolTransportException)?.InnerException != null)
                    e = ((ProtocolTransportException)e).InnerException!;

                LastPrimaryIssue.Record(new UpdatePrimaryFailed() { Exception = e }, Host, Services);
            }

            if (writesuccessful)
            {
                Services.Log(LogLevel.Debug, "write ({0} updates) success v{1}", updates.Count, version + updates.Count);

                // 现在我们通过应用相同的更新来更新缓存状态
                // 万一遇到例外 我们会重新阅读整个州 从储存
                try
                {
                    foreach (var u in updates)
                    {
                        version++;
                        Host.UpdateView(this.cached, u);
                    }

                    transitionssuccessful = true;
                }
                catch (Exception e)
                {
                    Services.CaughtUserCodeException("UpdateView", nameof(WriteAsync), e);
                }
            }

            if (!writesuccessful || !transitionssuccessful)
            {
                Services.Log(LogLevel.Debug, "{0} failed {1}", writesuccessful ? "transitions" : "write", LastPrimaryIssue);

                while (true) // 固执,直到我们重新读取国家 从储存状态
                {
                    await LastPrimaryIssue.DelayBeforeRetry();

                    try
                    {
                        var result = await ((ICustomStorageInterface<TLogView, TLogEntry>)Host).ReadStateFromStorage();
                        version = result.Key;
                        cached = result.Value;

                        Services.Log(LogLevel.Debug, "read success v{0}", version);

                        LastPrimaryIssue.Resolve(Host, Services);

                        break;
                    }
                    catch (Exception e)
                    {
                        // 被转发的un wrap 内例外 - 有助于调试
                        if ((e as ProtocolTransportException)?.InnerException != null)
                            e = ((ProtocolTransportException)e).InnerException!;

                        LastPrimaryIssue.Record(new ReadFromPrimaryFailed() { Exception = e }, Host, Services);
                    }

                    Services.Log(LogLevel.Debug, "read failed {0}", LastPrimaryIssue);
                }
            }

            exit_operation("WriteAsync");

            return writesuccessful ? updates.Count : 0;
        }

        /// <summary>
        /// 描述更新主贮存时出现的连接问题。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class UpdatePrimaryFailed : PrimaryOperationFailed
        {
            /// <inheritdoc/>
            public override string ToString()
            {
                return $"update primary failed: caught {Exception.GetType().Name}: {Exception.Message}";
            }
        }


        /// <summary>
        /// 描述读取主贮存时出现的连接问题。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        public sealed class ReadFromPrimaryFailed : PrimaryOperationFailed
        {
            /// <inheritdoc/>
            public override string ToString()
            {
                return $"read from primary failed: caught {Exception.GetType().Name}: {Exception.Message}";
            }
        }


        /// <summary>
        /// 在主机启动后发送至此 grain 远程实例的通知电文
        /// 更新后, 让他们知道最新版本。 包含所有应用过的更新 。
        /// </summary>
        [Serializable]
        [GenerateSerializer]
        protected internal sealed class UpdateNotificationMessage : INotificationMessage
        {
            /// <inheritdoc/>
            [Id(0)]
            public int Version { get; set; }

            /// <summary> 适用的最新清单。 </summary>
            [Id(1)]
            public List<TLogEntry> Updates { get; set; } = null!;

            /// <summary>
            /// 说明该通知电文适合追查。
            /// </summary>
            public override string ToString()
            {
                return string.Format("v{0} ({1} updates)", Version, Updates.Count);
            }
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
            while (notifications.Count > 0 && notifications.ElementAt(0).Key < version)
            {
                Services.Log(LogLevel.Debug, "discarding notification {0}", notifications.ElementAt(0).Value);
                notifications.RemoveAt(0);
            }

            // 反映下一个全球版本的处理通知
            while (notifications.Count > 0 && notifications.ElementAt(0).Key == version)
            {
                var updatenotification = notifications.ElementAt(0).Value;
                notifications.RemoveAt(0);

                // 应用所有待处理操作 
                foreach (var u in updatenotification.Updates)
                    try
                    {
                        Host.UpdateView(cached, u);
                    }
                    catch (Exception e)
                    {
                        Services.CaughtUserCodeException("UpdateView", nameof(ProcessNotifications), e);
                    }

                version = updatenotification.Version;

                Services.Log(LogLevel.Debug, "notification success ({0} updates) v{1}", updatenotification.Updates.Count, version);
            }

            Services.Log(LogLevel.Trace, "unprocessed notifications in queue: {0}", notifications.Count);

            base.ProcessNotifications();

        }

        [Conditional("DEBUG")]
        private void enter_operation(string name)
        {
            Services.Log(LogLevel.Trace, "/-- enter {0}", name);
        }

        [Conditional("DEBUG")]
        private void exit_operation(string name)
        {
            Services.Log(LogLevel.Trace, "\\-- exit {0}", name);
        }

    }
}
