using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Orleans.Core.Internal;

namespace Orleans.Runtime
{
    /// <summary>
    /// 本类持有有关目前正在处理的请求的信息。
    /// 明确打算将其用于应用代码。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 请求的背景作为财产袋来说明。
    /// 有些值是默认提供的; 其它值来自
    /// 请求导致当前处理。
    /// </para>
    /// <para>
    /// 储存在 <see cref="RequestContext"/> 传播自 Orleans 客户端到客户端 Orleans grains 自动由 Orleans 运行时间。
    /// </para>
    /// </remarks>
    public static class RequestContext
    {
        internal const string CALL_CHAIN_REENTRANCY_HEADER = "#CCR";
        internal const string PING_APPLICATION_HEADER = "Ping";

        internal static readonly AsyncLocal<ContextProperties> CallContextData = new();

        /// <summary>
        /// 获取或设置用于在当前呼叫链中允许重新响应的调用的标识符。
        /// </summary>
        public static Guid ReentrancyId
        {
            get => Get(CALL_CHAIN_REENTRANCY_HEADER) is Guid guid ? guid : Guid.Empty;
            set
            {
                if (value == Guid.Empty)
                {
                    Remove(CALL_CHAIN_REENTRANCY_HEADER);
                }
                else
                {
                    Set(CALL_CHAIN_REENTRANCY_HEADER, value);
                }
            }
        }

        /// <summary>
        /// 允许在处置所归还的 <see cref="ReentrancySection"/> 之前重新发出随后发出的电话。
        /// </summary>
        public static ReentrancySection AllowCallChainReentrancy()
        {
            var originalCallChainId = ReentrancyId;
            var newCallChainId = originalCallChainId == Guid.Empty ? Guid.NewGuid() : originalCallChainId;
            return new ReentrancySection(originalCallChainId, newCallChainId);
        }

        /// <summary>
        /// 处理退款 <see cref="ReentrancySection"/> 之前发出的后续电话,禁止重新打电话。
        /// </summary>
        public static ReentrancySection SuppressCallChainReentrancy() => new(ReentrancyId, Guid.Empty);

        /// <summary>
        /// 从请求上下文中获取一个值 。
        /// </summary>
        /// <param name="key">需要检索值的密钥。</param>
        /// <returns>
        /// 与所提供的密钥当前相关的价值,否则 <see langword="null"/> 如果该密钥没有数据, 则该密钥将不存在数据 。
        /// </returns>
        public static object? Get(string key)
        {
            var properties = CallContextData.Value;
            var values = properties.Values;

            if (values != null && values.TryGetValue(key, out var result))
            {
                return result;
            }

            return null;
        }

        /// <summary>
        /// 设定请求上下文中的值。
        /// </summary>
        /// <param name="key">要更新或添加值的密钥 。</param>
        /// <param name="value">将存储到请求上下文的价值。</param>
        public static void Set(string key, object value)
        {
            var properties = CallContextData.Value;
            var values = properties.Values;

            if (values == null)
            {
                values = new Dictionary<string, object>(1);
            }
            else
            {
                // 复制实际字典值, 将其变换并放回原位 。
                // 这是由于 AsyncLocal 副本链接到字典, 而不是创建新链接 。
                // 所以我们需要确保改变价值, 我们不会影响其他线索。
                var hadPreviousValue = values.ContainsKey(key);
                var newValues = new Dictionary<string, object>(values.Count + (hadPreviousValue ? 0 : 1));
                foreach (var pair in values)
                {
                    newValues.Add(pair.Key, pair.Value);
                }

                values = newValues;
            }

            values[key] = value;
            CallContextData.Value = new ContextProperties
            {
                Values = values
            };
        }

        /// <summary>
        /// 从请求上下文中删除值 。
        /// </summary>
        /// <param name="key">要删除值的密钥。</param>
        /// <returns><see langword="true"/> 如果该数值先前是在请求的上下文中,而现在已被删除,则 <see langword="false"/>.</returns>
        public static bool Remove(string key)
        {
            var properties = CallContextData.Value;
            var values = properties.Values;

            if (values == null || values.Count == 0 || !values.ContainsKey(key))
            {
                return false;
            }

            if (values.Count == 1)
            {
                CallContextData.Value = new ContextProperties
                {
                    Values = null
                };
                return true;
            }
            else
            {
                var newValues = new Dictionary<string, object>(values);
                newValues.Remove(key);
                CallContextData.Value = new ContextProperties
                {
                    Values = newValues
                };
                return true;
            }
        }

        /// <summary>
        /// 清除当前请求的背景。
        /// </summary>
        public static void Clear()
        {
            // 删除防止其值从此点传过的关键键
            if (!CallContextData.Value.IsDefault)
            {
                CallContextData.Value = default;
            }
        }

        /// <summary>
        /// 获取当前请求上下文中值的密钥收藏。
        /// </summary>
        public static IEnumerable<string> Keys => CallContextData.Value.Values?.Keys ?? Enumerable.Empty<string>();

        /// <summary>
        /// 获取当前请求上下文中的条目收藏。
        /// </summary>
        public static IEnumerable<KeyValuePair<string, object>> Entries => CallContextData.Value.Values ?? Enumerable.Empty<KeyValuePair<string, object>>();

        internal readonly struct ContextProperties
        {
            public Dictionary<string, object>? Values { get; init; }
            public bool IsDefault => Values is null;
        }

        /// <summary>
        /// 表示对呼叫链回放的幅度变化。
        /// </summary>
        [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This disposable scope token controls runtime reentrancy state and does not represent a comparable value.")]
        public readonly struct ReentrancySection : IDisposable
        {
            private readonly Guid _originalReentrancyId;
            private readonly Guid _newReentrancyId;

            /// <summary>
            /// 初始化 <see cref="ReentrancySection"/> 结构的新实例。
            /// </summary>
            /// <param name="originalReentrancyId">处理此段时要恢复的再生识别符 。</param>
            /// <param name="newReentrancyId">用于该节的再生识别符号。</param>
            public ReentrancySection(Guid originalReentrancyId, Guid newReentrancyId)
            {
                _originalReentrancyId = originalReentrancyId;
                _newReentrancyId = newReentrancyId;

                if (newReentrancyId != originalReentrancyId)
                {
                    ReentrancyId = newReentrancyId;
                }

                if (newReentrancyId != Guid.Empty)
                {
                    var grain = RuntimeContext.Current as ICallChainReentrantGrainContext;
                    grain?.OnEnterReentrantSection(_newReentrancyId);
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (_newReentrancyId != Guid.Empty)
                {
                    var grain = RuntimeContext.Current as ICallChainReentrantGrainContext;
                    grain?.OnExitReentrantSection(_newReentrancyId);
                }

                if (_newReentrancyId != _originalReentrancyId)
                {
                    ReentrancyId = _originalReentrancyId;
                }
            }
        }
    }
}
