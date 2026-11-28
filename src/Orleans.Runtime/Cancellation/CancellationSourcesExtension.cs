using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using Orleans.Serialization.Invocation;

namespace Orleans.Runtime
{
#pragma warning disable CS0618 // 此扩展为旧版令牌提供分布式取消功能。
    /// <summary>
    /// 包含与令牌对应的取消令牌源列表，这些令牌
    /// 被传递给相关 Grain 激活实例。
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
        /// <param name="loggerFactory">日志记录器工厂。</param>
        /// <param name="cancellationRuntime">取消运行时。</param>
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

                // 仍然记录取消操作，以防原本会注册取消的调用仍处于待处理状态。
                this.RecordCancellationToken(tokenId, isCancellationRequested: true);
                return Task.CompletedTask;
            }

            entry.Touch();
            var token = entry.Token;
            return token.Cancel();
        }

        /// <summary>
        /// 将 <see cref="CancellationToken"/> 添加到 Grain 扩展，使其可通过远程调用 CancellationSourcesExtension 被取消。
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

                // 将通过网络收到、尚未完整初始化的 GrainCancellationToken 替换为本地完整创建的实例。
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
