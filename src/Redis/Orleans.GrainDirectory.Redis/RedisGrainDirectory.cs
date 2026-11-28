using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using StackExchange.Redis;

namespace Orleans.GrainDirectory.Redis
{
    /// <summary>
    /// 提供由 Redis 支持的 Grain 目录。
    /// </summary>
    /// <remarks>
    /// 目录查找和注册变更操作使用 Redis 主节点。
    /// </remarks>
    public partial class RedisGrainDirectory : IGrainDirectory, ILifecycleParticipant<ISiloLifecycle>, IDisposable, IAsyncDisposable
    {
        private readonly RedisGrainDirectoryOptions _directoryOptions;
        private readonly ClusterOptions _clusterOptions;
        private readonly ILogger<RedisGrainDirectory> _logger;
        private readonly RedisKey _keyPrefix;
        private readonly string _ttl;

        // 两者均在 Initialize 方法中初始化。
        private IConnectionMultiplexer _redis = null!;
        private IDatabase _database = null!;
        private bool _redisIsShared;

        private bool _disposed;

        /// <summary>
        /// 初始化 <see cref="RedisGrainDirectory"/> 类的新实例。
        /// </summary>
        /// <param name="directoryOptions">Redis Grain 目录选项。</param>
        /// <param name="clusterOptions">集群选项。</param>
        /// <param name="logger">日志记录器。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="directoryOptions"/>、<paramref name="clusterOptions"/> 或 <paramref name="logger"/> 为 <see langword="null"/>。
        /// </exception>
        public RedisGrainDirectory(
            RedisGrainDirectoryOptions directoryOptions,
            IOptions<ClusterOptions> clusterOptions,
            ILogger<RedisGrainDirectory> logger)
        {
            ArgumentNullException.ThrowIfNull(directoryOptions);
            ArgumentNullException.ThrowIfNull(clusterOptions);
            ArgumentNullException.ThrowIfNull(logger);

            _directoryOptions = directoryOptions;
            _logger = logger;
            _clusterOptions = clusterOptions.Value;
            _keyPrefix = Encoding.UTF8.GetBytes($"{_clusterOptions.ClusterId}/directory/");
            _ttl = directoryOptions.EntryExpiry is { } ts ? ts.TotalSeconds.ToString(CultureInfo.InvariantCulture) : "-1";
        }

        /// <summary>
        /// 查找 Grain 的已注册激活实例。
        /// </summary>
        /// <param name="grainId">Grain 标识符。</param>
        /// <returns>已注册的 Grain 地址；如果没有注册，则为 <see langword="null"/>。</returns>
        public async Task<GrainAddress?> Lookup(GrainId grainId)
        {
            try
            {
                var result = _disposed ? null : (string?)await _database.StringGetAsync(GetKey(grainId), CommandFlags.DemandMaster);

                LogDebugLookup(grainId, string.IsNullOrWhiteSpace(result) ? "null" : result);

                if (string.IsNullOrWhiteSpace(result))
                    return default;

                return JsonSerializer.Deserialize<GrainAddress>(result);
            }
            catch (Exception ex)
            {
                LogErrorLookupFailed(ex, grainId);

                if (IsRedisException(ex))
                    throw new OrleansException($"Lookup failed for {grainId} : {ex}");
                else
                    throw;
            }
        }

        /// <summary>
        /// 如果 Grain 尚未注册，则注册其激活实例。
        /// </summary>
        /// <param name="address">要注册的 Grain 地址。</param>
        /// <returns>在目录中注册的 Grain 地址。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public Task<GrainAddress?> Register(GrainAddress address) => Register(address, null);

        /// <summary>
        /// 注册 Grain 激活实例，并在指定的先前注册仍为当前注册时将其替换。
        /// </summary>
        /// <param name="address">要注册的 Grain 地址。</param>
        /// <param name="previousAddress">要替换的先前注册；若要求条目为空，则为 <see langword="null"/>。</param>
        /// <returns>在目录中注册的 Grain 地址。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public async Task<GrainAddress?> Register(GrainAddress address, GrainAddress? previousAddress)
        {
            ArgumentNullException.ThrowIfNull(address);

            const string RegisterScript =
                """
                local cur = redis.call('GET', KEYS[1])
                local success = true
                if cur ~= false then
                    local typedCur = cjson.decode(cur)
                    if typedCur.ActivationId ~= ARGV[2] then
                       success = false
                    end
                end

                if (success) then
                    redis.call('SET', KEYS[1], ARGV[1])
                    if ARGV[3] ~= '-1' then
                        redis.call('EXPIRE', KEYS[1], ARGV[3])
                    end
                    return nil
                end

                return cur
                """;

            var value = JsonSerializer.Serialize(address);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, _database);

                var previousActivationId = previousAddress is { } ? previousAddress.ActivationId.ToString() : "";
                var key = GetKey(address.GrainId);
                var entryString = (string?)await _database.ScriptEvaluateAsync(
                    RegisterScript,
                    keys: new RedisKey[] { key },
                    values: new RedisValue[] { value, previousActivationId, _ttl },
                    flags: CommandFlags.DemandMaster)!;

                if (entryString is null)
                {
                    LogDebugRegistered(address.GrainId, value);

                    return address;
                }

                LogDebugRegisterFailed(address.GrainId, value, entryString);

                return JsonSerializer.Deserialize<GrainAddress>(entryString);
            }
            catch (Exception ex)
            {
                LogErrorRegisterFailed(ex, address.GrainId, value);

                if (IsRedisException(ex))
                {
                    throw new OrleansException($"Register failed for {address.GrainId} ({value}) : {ex}");
                }
                else
                {
                    throw;
                }
            }
        }

        /// <summary>
        /// 当存储的激活实例标识符与传入地址匹配时，移除该 Grain 激活实例。
        /// </summary>
        /// <param name="address">要移除的 Grain 地址。</param>
        /// <returns>表示该操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> 为 <see langword="null"/>。</exception>
        public async Task Unregister(GrainAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);

            const string DeleteScript =
                """
                local cur = redis.call('GET', KEYS[1])
                if cur ~= false then
                    local typedCur = cjson.decode(cur)
                    if typedCur.ActivationId == ARGV[1] then
                        return redis.call('DEL', KEYS[1])
                    end
                end
                return 0
                """;

            try
            {
                ObjectDisposedException.ThrowIf(_disposed, _database);

                var result = (int)await _database.ScriptEvaluateAsync(
                    DeleteScript,
                    keys: new RedisKey[] { GetKey(address.GrainId) },
                    values: new RedisValue[] { address.ActivationId.ToString() },
                    flags: CommandFlags.DemandMaster);

                LogDebugUnregister(address.GrainId, new(address), (result != 0) ? "OK" : "Conflict");
            }
            catch (Exception ex)
            {
                LogErrorUnregisterFailed(ex, address.GrainId, new(address));

                if (IsRedisException(ex))
                    throw new OrleansException($"Unregister failed for {address.GrainId} ({JsonSerializer.Serialize(address)}) : {ex}");
                else
                    throw;
            }
        }

        /// <summary>
        /// 完成 Silo 清理回调。Redis 条目通过特定激活实例的注销操作及可选的过期机制进行清理。
        /// </summary>
        /// <param name="siloAddresses">与清理回调关联的 Silo 地址。</param>
        /// <returns>已完成的任务。</returns>
        public Task UnregisterSilos(List<SiloAddress> siloAddresses)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 将 Grain 目录订阅到 Silo 生命周期。
        /// </summary>
        /// <param name="lifecycle">Silo 生命周期。</param>
        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(nameof(RedisGrainDirectory), ServiceLifecycleStage.RuntimeInitialize, Initialize);
        }

        /// <summary>
        /// 创建配置的 Redis 连接，并选择其数据库。
        /// </summary>
        /// <param name="ct">Silo 生命周期提供的取消令牌。</param>
        /// <returns>表示初始化操作的任务。</returns>
        public async Task Initialize(CancellationToken ct = default)
        {
            (_redis, _redisIsShared) = await _directoryOptions.CreateMultiplexer(_directoryOptions);

            // 配置日志记录。
            _redis.ConnectionRestored += LogConnectionRestored;
            _redis.ConnectionFailed += LogConnectionFailed;
            _redis.ErrorMessage += LogErrorMessage;
            _redis.InternalError += LogInternalError;

            _database = _redis.GetDatabase();
        }

        /// <summary>
        /// 解除事件订阅，并释放由提供程序拥有的 Redis 连接。
        /// </summary>
        public void Dispose()
        {
            _disposed = true;

            var redis = _redis;
            if (redis is null)
            {
                return;
            }

            var redisIsShared = _redisIsShared;
            redis.ConnectionRestored -= LogConnectionRestored;
            redis.ConnectionFailed -= LogConnectionFailed;
            redis.ErrorMessage -= LogErrorMessage;
            redis.InternalError -= LogInternalError;
            _redis = null!;
            _database = null!;
            _redisIsShared = false;

            if (!redisIsShared)
            {
                redis.Dispose();
            }
        }

        /// <summary>
        /// 解除事件订阅，并异步释放由提供程序拥有的 Redis 连接。
        /// </summary>
        /// <returns>表示释放操作的 ValueTask。</returns>
        public async ValueTask DisposeAsync()
        {
            _disposed = true;

            var redis = _redis;
            if (redis is null)
            {
                return;
            }

            var redisIsShared = _redisIsShared;
            redis.ConnectionRestored -= LogConnectionRestored;
            redis.ConnectionFailed -= LogConnectionFailed;
            redis.ErrorMessage -= LogErrorMessage;
            redis.InternalError -= LogInternalError;
            _redis = null!;
            _database = null!;
            _redisIsShared = false;

            if (!redisIsShared)
            {
                await redis.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal RedisKey GetKey(GrainId grainId) => _keyPrefix.Append(grainId.ToString());

        #region Logging
        private void LogConnectionRestored(object? sender, ConnectionFailedEventArgs e)
            => LogInfoConnectionRestored(e.Exception, e.EndPoint, e.FailureType);

        private void LogConnectionFailed(object? sender, ConnectionFailedEventArgs e)
            => LogErrorConnectionFailed(e.Exception, e.EndPoint, e.FailureType);

        private void LogErrorMessage(object? sender, RedisErrorEventArgs e)
            => LogErrorRedisMessage(e.Message);

        private void LogInternalError(object? sender, InternalErrorEventArgs e)
            => LogErrorInternalError(e.Exception);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Lookup {GrainId}: {Result}"
        )]
        private partial void LogDebugLookup(GrainId grainId, string result);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Lookup failed for {GrainId}"
        )]
        private partial void LogErrorLookupFailed(Exception exception, GrainId grainId);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Registered {GrainId} ({Address})"
        )]
        private partial void LogDebugRegistered(GrainId grainId, string address);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Failed to register {GrainId} ({Address}) in directory: Conflicted with existing value, {Result}"
        )]
        private partial void LogDebugRegisterFailed(GrainId grainId, string address, string result);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Failed to register {GrainId} ({Address}) in directory"
        )]
        private partial void LogErrorRegisterFailed(Exception exception, GrainId grainId, string address);

        private readonly struct GrainAddressLogRecord(GrainAddress address)
        {
            public override string ToString() => JsonSerializer.Serialize(address);
        }

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Unregister {GrainId} ({Address}): {Result}"
        )]
        private partial void LogDebugUnregister(GrainId grainId, GrainAddressLogRecord address, string result);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Unregister failed for {GrainId} ({Address})"
        )]
        private partial void LogErrorUnregisterFailed(Exception exception, GrainId grainId, GrainAddressLogRecord address);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Connection to {EndPoint} restored: {FailureType}"
        )]
        private partial void LogInfoConnectionRestored(Exception? exception, EndPoint? endPoint, ConnectionFailureType failureType);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Connection to {EndPoint} failed: {FailureType}"
        )]
        private partial void LogErrorConnectionFailed(Exception? exception, EndPoint? endPoint, ConnectionFailureType failureType);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "{Message}"
        )]
        private partial void LogErrorRedisMessage(string message);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Internal error"
        )]
        private partial void LogErrorInternalError(Exception? exception);
        #endregion

        // 客户端无法序列化这些异常。
        private static bool IsRedisException(Exception ex) => ex is RedisException || ex is RedisTimeoutException || ex is RedisCommandException;
    }
}
