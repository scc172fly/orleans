using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
#pragma warning disable CS0618 // This extension provides distributed cancellation for the legacy token.
    /// <summary>
    /// 包含与此牌相对应的取消代号源列表
    /// 传递到相关的 grain 激活。
    /// </summary>
    internal partial class CancellationSourcesExtension : ICancellationSourcesExtension, IDisposable
    {
        private readonly ConcurrentDictionary<Guid, Entry> _cancellationTokens = new ConcurrentDictionary<Guid, Entry>();
        private readonly ILogger _logger;
        private readonly IGrainCancellationTokenRuntime _cancellationTokenRuntime;
        private readonly Timer _cleanupTimer;
        private readonly Func<Guid, Entry> _createToken;
        private static readonly TimeSpan _cleanupFrequency = TimeSpan.FromMinutes(7);

        /// <summary>
        /// 初始化 <see cref="CancellationSourcesExtension"/> 类的新实例。
        /// </summary>
        /// <param name="loggerFactory">伐木厂</param>
        /// <param name="cancellationRuntime">取消运行时间。</param>
        public CancellationSourcesExtension(ILoggerFactory loggerFactory, IGrainCancellationTokenRuntime cancellationRuntime)
        {
            _logger = loggerFactory.CreateLogger<CancellationSourcesExtension>();
            _cancellationTokenRuntime = cancellationRuntime;
            _cleanupTimer = new Timer(obj => ((CancellationSourcesExtension)obj!).ExpireTokens(), this, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            _createToken = id => new Entry(new GrainCancellationToken(id, false, _cancellationTokenRuntime));
        }

        /// <inheritdoc />
        public Task CancelRemoteToken(Guid tokenId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_cancellationTokens.TryGetValue(tokenId, out var entry))
            {
                LogCancellationFailed(tokenId);

                // 无论如何记录取消,如果登记取消的电话仍未接到,则要记录取消。
                this.RecordCancellationToken(tokenId, isCancellationRequested: true);
                return Task.CompletedTask;
            }

            entry.Touch();
            var token = entry.Token;
            return token.Cancel();
        }

        /// <summary>
        /// 将 <see cref="CancellationToken"/> 添加到 grain 扩展名中,以便通过远程呼叫 CancellationSourcesExtension取消该扩展名。
        /// </summary>
        /// <param name="target"></param>
        /// <param name="request"></param>
        internal static void RegisterCancellationTokens(
            IGrainContext target,
            IInvokable request)
        {
            if (request is RequestBase { MayHaveGrainCancellationToken: false })
            {
                return;
            }

            var argumentCount = request.GetArgumentCount();
            for (var i = 0; i < argumentCount; i++)
            {
                var arg = request.GetArgument(i);
                if (arg is not GrainCancellationToken grainToken)
                {
                    continue;
                }

                var cancellationExtension = (CancellationSourcesExtension)target.GetGrainExtension<ICancellationSourcesExtension>();

                // 取代了当地完全创建的半份烤烤 GrainCancellationToken 。
                request.SetArgument(i, cancellationExtension.RecordCancellationToken(grainToken.Id, grainToken.IsCancellationRequested));
            }
        }

        private GrainCancellationToken RecordCancellationToken(Guid tokenId, bool isCancellationRequested)
        {
            if (_cancellationTokens.TryGetValue(tokenId, out var entry))
            {
                entry.Touch();
                return entry.Token;
            }

            entry = _cancellationTokens.GetOrAdd(tokenId, _createToken);
            if (isCancellationRequested)
            {
                entry.Token.Cancel();
            }

            return entry.Token;
        }

        private void ExpireTokens()
        {
            var now = Stopwatch.GetTimestamp();
            foreach (var token in _cancellationTokens)
            {
                if (token.Value.IsExpired(_cleanupFrequency, now))
                {
                    _cancellationTokens.TryRemove(token.Key, out _);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cleanupTimer.Dispose();
        }

        private class Entry
        {
            private long _createdTime;

            public Entry(GrainCancellationToken token)
            {
                Token = token;
                _createdTime = Stopwatch.GetTimestamp();
            }

            public void Touch() => _createdTime = Stopwatch.GetTimestamp();

            public GrainCancellationToken Token { get; }

            public bool IsExpired(TimeSpan expiry, long nowTimestamp)
            {
                var untouchedTime = TimeSpan.FromSeconds((nowTimestamp - _createdTime) / (double)Stopwatch.Frequency);

                return untouchedTime >= expiry;
            }
        }

        [LoggerMessage(
            EventId = (int)ErrorCode.CancellationTokenCancelFailed,
            Level = LogLevel.Warning,
            Message = "Received a cancel call for token with id '{TokenId}', but the token was not found."
        )]
        private partial void LogCancellationFailed(Guid tokenId);
    }
#pragma warning restore CS0618
}
