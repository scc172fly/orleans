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
            var state = stateStorage!.State!; // ReadStateAsync 初始化交易状态记录。
            return new TransactionalStorageLoadResponse<TState>(stateStorage!.Etag, state.CommittedState, state.CommittedSequenceId, state.Metadata, state.PendingStates); // 访问超过StateStorage 将字段初始化。
        }

        public async Task<string> Store(string? expectedETag, TransactionalStateMetaData metadata, List<PendingTransactionState<TState>>? statesToPrepare, long? commitUpTo, long? abortAfter)
        {
            if (this.StateStorage.Etag != expectedETag)
                throw new ArgumentException("Etag does not match", nameof(expectedETag));
            var storedState = stateStorage!.State!; // 访问量超过StateStorage ,初始化字段,且 ReadStateAsync 已进入状态。
            var storedETag = stateStorage.Etag;
            var state = new TransactionalStateRecord<TState>
            {
                CommittedState = storedState.CommittedState,
                CommittedSequenceId = storedState.CommittedSequenceId,
                Metadata = metadata,
                PendingStates = new List<PendingTransactionState<TState>>(storedState.PendingStates),
            };

            var pendinglist = state.PendingStates;

            // 中止中断
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
                        pendinglist.Add(p); //附加附件(附件)
                    }
                    else if (pendinglist[pos].SequenceId == p.SequenceId)
                    {
                        pendinglist[pos] = p;  //替换
                    }
                    else
                    {
                        pendinglist.Insert(pos, p); //插入
                    }
                }
            }

            // 承诺
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
    /// 表示使用 <see cref="IGrainStorage"/> 提供者存储的交易状态。
    /// </summary>
    /// <typeparam name="TState">交易状态类型。</typeparam>
    [Serializable]
    [GenerateSerializer]
    public sealed class TransactionalStateRecord<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 获取或设置最近承诺状态。
        /// </summary>
        [Id(0)]
        public TState CommittedState { get; set; } = new TState();

        /// <summary>
        /// 获取或设置最近承诺状态的序列编号。
        /// </summary>
        [Id(1)]
        public long CommittedSequenceId { get; set; }

        /// <summary>
        /// 获取或设置交易协议元数据。
        /// </summary>
        [Id(2)]
        public TransactionalStateMetaData Metadata { get; set; } = new TransactionalStateMetaData();

        /// <summary>
        /// 获取或设置未执行的预设状态版本。
        /// </summary>
        [Id(3)]
        public List<PendingTransactionState<TState>> PendingStates { get; set; } = new List<PendingTransactionState<TState>>();
    }
}
