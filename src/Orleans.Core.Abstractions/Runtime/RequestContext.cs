using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Orleans.Core.Internal;

namespace Orleans.Runtime
{
    /// <summary>
    /// 此类保存当前正在处理的请求的相关信息。
    /// 它专门设计为可由应用程序代码访问。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 请求上下文表示为一个属性包。
    /// 部分值由系统默认提供，其余值来自触发当前处理过程的
    /// 请求的消息头。
    /// </para>
    /// <para>
    /// 存储在 <see cref="RequestContext"/> 中的信息由 Orleans 运行时自动从 Orleans 客户端传播到 Orleans Grain。
    /// </para>
    /// </remarks>
    public static class RequestContext
    {
        internal const string CALL_CHAIN_REENTRANCY_HEADER = "#CCR";
        internal const string PING_APPLICATION_HEADER = "Ping";

        internal static readonly AsyncLocal<ContextProperties> CallContextData = new();

        /// <summary>
        /// 获取或设置用于允许当前调用链中发生可重入调用的标识符。
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
        /// 允许在返回的 <see cref="ReentrancySection"/> 被释放之前发出的后续调用发生重入。
        /// </summary>
        public static ReentrancySection AllowCallChainReentrancy()
        {
            var originalCallChainId = ReentrancyId;
            var newCallChainId = originalCallChainId == Guid.Empty ? Guid.NewGuid() : originalCallChainId;
            return new ReentrancySection(originalCallChainId, newCallChainId);
        }

        /// <summary>
        /// 禁止在返回的 <see cref="ReentrancySection"/> 被释放之前发出的后续调用发生重入。
        /// </summary>
        public static ReentrancySection SuppressCallChainReentrancy() => new(ReentrancyId, Guid.Empty);

        /// <summary>
        /// 从请求上下文中检索值。
        /// </summary>
        /// <param name="key">要检索的值对应的键。</param>
        /// <returns>
        /// 当前与给定键关联的值；如果该键没有对应的数据，则返回 <see langword="null"/>。
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
        /// 在请求上下文中设置值。
        /// </summary>
        /// <param name="key">要更新或添加的值对应的键。</param>
        /// <param name="value">要存入请求上下文的值。</param>
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
                // 必须复制实际的 Dictionary 值，修改副本后再将其设置回去。
                // 这是因为 AsyncLocal 复制的是对字典的引用，而不是创建新字典。
                // 因此必须确保修改值时不会影响其他线程。
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
        /// 从请求上下文中移除一个值。
        /// </summary>
        /// <param name="key">要移除的值对应的键。</param>
        /// <returns>如果该值此前存在于请求上下文中且现已移除，则为 <see langword="true"/>；否则为 <see langword="false"/>。</returns>
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
        /// 清空当前请求上下文。
        /// </summary>
        public static void Clear()
        {
            // 移除该键，以免从此处起继续传递其值。
            if (!CallContextData.Value.IsDefault)
            {
                CallContextData.Value = default;
            }
        }

        /// <summary>
        /// 获取请求上下文中当前各值对应的键集合。
        /// </summary>
        public static IEnumerable<string> Keys => CallContextData.Value.Values?.Keys ?? Enumerable.Empty<string>();

        /// <summary>
        /// 获取请求上下文中当前的条目集合。
        /// </summary>
        public static IEnumerable<KeyValuePair<string, object>> Entries => CallContextData.Value.Values ?? Enumerable.Empty<KeyValuePair<string, object>>();

        internal readonly struct ContextProperties
        {
            public Dictionary<string, object>? Values { get; init; }
            public bool IsDefault => Values is null;
        }

        /// <summary>
        /// 表示在特定作用域内对调用链可重入性的修改。
        /// </summary>
        [SuppressMessage("Performance", "CA1815:Override equals and operator equals on value types", Justification = "This disposable scope token controls runtime reentrancy state and does not represent a comparable value.")]
        public readonly struct ReentrancySection : IDisposable
        {
            private readonly Guid _originalReentrancyId;
            private readonly Guid _newReentrancyId;

            /// <summary>
            /// 初始化 <see cref="ReentrancySection"/> 结构的新实例。
            /// </summary>
            /// <param name="originalReentrancyId">释放此作用域时要恢复的可重入性标识符。</param>
            /// <param name="newReentrancyId">此作用域内使用的可重入性标识符。</param>
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
