using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Core;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    internal sealed class TransactionalStateStorageProviderWrapper<TState> : ITransactionalStateStorage<TState>
        where TState : class, new()
    {
        private readonly IGrainStorage grainStorage;
        private readonly IGrainContext context;
        private readonly string stateName;

        private StateStorageBridge<TransactionalStateRecord<TState>>? stateStorage;
        [MemberNotNull(nameof(stateStorage))]
        private StateStorageBridge<TransactionalStateRecord<TState>> StateStorage => stateStorage ??= GetStateStorage();

        public TransactionalStateStorageProviderWrapper(IGrainStorage grainStorage, string stateName, IGrainContext context)
        {
            this.grainStorage = grainStorage;
            this.context = context;
            this.stateName = stateName;
        }

        public async Task<TransactionalStorageLoadResponse<TState>> Load()
        {
            await this.StateStorage.ReadStateAsync();
            var state = stateStorage!.State!; // ReadStateAsync 初始化事务状态记录。
            return new TransactionalStorageLoadResponse<TState>(stateStorage!.Etag, state.CommittedState, state.CommittedSequenceId, state.Metadata, state.PendingStates); // 上方对 StateStorage 的访问已初始化此字段。
        }

        public async Task<string> Store(string? expectedETag, TransactionalStateMetaData metadata, List<PendingTransactionState<TState>>? statesToPrepare, long? commitUpTo, long? abortAfter)
        {
            if (this.StateStorage.Etag != expectedETag)
                throw new ArgumentException("Etag does not match", nameof(expectedETag));
            var storedState = stateStorage!.State!; // 上方对 StateStorage 的访问已初始化此字段，而 ReadStateAsync 已填充其状态。
            var storedETag = stateStorage.Etag;
            var state = new TransactionalStateRecord<TState>
            {
                CommittedState = storedState.CommittedState,
                CommittedSequenceId = storedState.CommittedSequenceId,
                Metadata = metadata,
                PendingStates = new List<PendingTransactionState<TState>>(storedState.PendingStates),
            };

            var pendinglist = state.PendingStates;

            // 中止
            if (abortAfter.HasValue && pendinglist.Count != 0)
            {
                var pos = pendinglist.FindIndex(t => t.SequenceId > abortAfter.Value);
                if (pos != -1)
                {
                    pendinglist.RemoveRange(pos, pendinglist.Count - pos);
                }
            }

            // 准备
            if (statesToPrepare?.Count > 0)
            {
                foreach (var p in statesToPrepare)
                {
                    var pos = pendinglist.FindIndex(t => t.SequenceId >= p.SequenceId);
                    if (pos == -1)
                    {
                        pendinglist.Add(p); // 追加
                    }
                    else if (pendinglist[pos].SequenceId == p.SequenceId)
                    {
                        pendinglist[pos] = p;  // 替换
                    }
                    else
                    {
                        pendinglist.Insert(pos, p); // 插入
                    }
                }
            }

            // 提交
            if (commitUpTo.HasValue && commitUpTo.Value > state.CommittedSequenceId)
            {
                var pos = pendinglist.FindIndex(t => t.SequenceId == commitUpTo.Value);
                if (pos != -1)
                {
                    var committedState = pendinglist[pos];
                    state.CommittedSequenceId = committedState.SequenceId;
                    state.CommittedState = committedState.State;
                    pendinglist.RemoveRange(0, pos + 1);
                }
                else
                {
                    throw new InvalidOperationException($"Transactional state corrupted. Missing prepare record (SequenceId={commitUpTo.Value}) for committed transaction.");
                }
            }

            stateStorage.State = state;
            var writeSucceeded = false;
            try
            {
                await stateStorage.WriteStateAsync();
                var result = stateStorage.Etag ?? throw new InvalidOperationException("The grain storage provider did not supply an ETag after writing transactional state.");
                writeSucceeded = true;
                return result;
            }
            finally
            {
                if (!writeSucceeded)
                {
                    stateStorage.State = storedState;
                    stateStorage.Etag = storedETag;
                }
            }
        }

        private StateStorageBridge<TransactionalStateRecord<TState>> GetStateStorage()
        {
            return new(this.stateName, context, grainStorage);
        }
    }

    /// <summary>
    /// 表示使用 <see cref="IGrainStorage"/> 提供程序存储的事务状态。
    /// </summary>
    /// <typeparam name="TState">事务状态类型。</typeparam>
    [Serializable]
    [GenerateSerializer]
    public sealed class TransactionalStateRecord<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 获取或设置最近一次提交的状态。
        /// </summary>
        [Id(0)]
        public TState CommittedState { get; set; } = new TState();

        /// <summary>
        /// 获取或设置最近一次提交状态的序列号。
        /// </summary>
        [Id(1)]
        public long CommittedSequenceId { get; set; }

        /// <summary>
        /// 获取或设置事务协议元数据。
        /// </summary>
        [Id(2)]
        public TransactionalStateMetaData Metadata { get; set; } = new TransactionalStateMetaData();

        /// <summary>
        /// 获取或设置尚未提交的已准备状态版本。
        /// </summary>
        [Id(3)]
        public List<PendingTransactionState<TState>> PendingStates { get; set; } = new List<PendingTransactionState<TState>>();
    }
}
