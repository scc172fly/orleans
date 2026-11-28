using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using AwesomeAssertions.Equivalency;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 验证事务状态存储在准备、确认、取消和替换状态时的语义。
    /// </summary>
    /// <typeparam name="TState">事务存储实现所持久化的状态类型。</typeparam>
    public abstract class TransactionalStateStorageTestRunner<TState> : TransactionTestRunnerBase
        where TState : class, new()
    {
        /// <summary>
        /// 创建初始存储状态为空的事务状态存储实例。
        /// </summary>
        protected Func<Task<ITransactionalStateStorage<TState>>> stateStorageFactory;

        /// <summary>
        /// 根据整数种子创建测试状态值。
        /// </summary>
        protected Func<int, TState> stateFactory;

        /// <summary>
        /// 配置持久化状态值的等价性比较。
        /// </summary>
        protected Func<EquivalencyOptions<TState>, EquivalencyOptions<TState>>? assertConfig;

        /// <summary>
        /// 初始化 <see cref="TransactionalStateStorageTestRunner{TState}"/> 类的新实例。
        /// </summary>
        /// <param name="stateStorageFactory">创建初始状态为空的存储实例的工厂。</param>
        /// <param name="stateFactory">根据整数种子创建测试状态值的工厂。</param>
        /// <param name="grainFactory">测试运行器使用的 Grain 工厂。</param>
        /// <param name="testOutput">用于写入测试输出的回调。</param>
        /// <param name="assertConfig">用于配置状态等价性比较的可选回调。</param>
        protected TransactionalStateStorageTestRunner(Func<Task<ITransactionalStateStorage<TState>>> stateStorageFactory, Func<int, TState> stateFactory,
            IGrainFactory grainFactory, Action<string> testOutput,
            Func<EquivalencyOptions<TState>, EquivalencyOptions<TState>>? assertConfig = null)
            : base(grainFactory, testOutput)
        {
            this.stateStorageFactory = stateStorageFactory;
            this.stateFactory = stateFactory;
            this.assertConfig = assertConfig;
        }

        /// <summary>
        /// 验证首次加载返回初始已提交状态，不包含待处理状态，也没有 ETag。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task FirstTime_Load_ShouldReturnEmptyLoadResponse()
        {
            var stateStorage = await this.stateStorageFactory();
            var response = await stateStorage.Load();
            var defaultStateValue = new TState();

            // 断言
            response.Should().NotBeNull();
            response.ETag.Should().BeNull();
            response.CommittedSequenceId.Should().Be(0);
            AssertTState(response.CommittedState, defaultStateValue);
            response.PendingStates.Should().BeEmpty();
        }

        private static readonly List<PendingTransactionState<TState>> emptyPendingStates = new List<PendingTransactionState<TState>>();

        /// <summary>
        /// 验证存储未改变的状态会创建 ETag 并持久化元数据，而不会改变状态。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task StoreWithoutChanges()
        {
            var stateStorage = await this.stateStorageFactory();

            // 首次加载
            var loadresponse = await stateStorage.Load();

            // 在没有任何变更的情况下存储
            var etag1 = await stateStorage.Store(loadresponse.ETag, loadresponse.Metadata, emptyPendingStates, null, null);
            etag1.Should().NotBeNullOrEmpty();

            // 再次加载
            loadresponse = await stateStorage.Load();
            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Should().BeEmpty();
            loadresponse.ETag.Should().Be(etag1);
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Should().BeEmpty();

            // 更新元数据，然后写回
            var now = DateTime.UtcNow;
            var cr = MakeCommitRecords(2, 2);
            var metadata = new TransactionalStateMetaData() { TimeStamp = now, CommitRecords = cr };
            var etag2 = await stateStorage.Store(etag1, metadata, emptyPendingStates, null, null);
            etag2.Should().NotBeNullOrEmpty();

            // 再次加载并检查内容
            loadresponse = await stateStorage.Load();
            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.Metadata.TimeStamp.Should().Be(now);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(cr.Count);
            loadresponse.ETag.Should().Be(etag2);
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Should().BeEmpty();
        }

        /// <summary>
        /// 验证无效或过期的 ETag 会导致写入被拒绝，并保留先前存储的值。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task WrongEtags()
        {
            var stateStorage = await this.stateStorageFactory();

            // 首次加载
            var loadresponse = await stateStorage.Load();

            // 使用错误的 ETag 存储，必须失败
            Func<Task> storeWithWrongETag = () => stateStorage.Store(
                "wrong-etag",
                loadresponse.Metadata,
                emptyPendingStates,
                null,
                null);
            await storeWithWrongETag.Should().ThrowAsync<Exception>("the ETag does not identify the loaded version");

            // 再次加载并验证被拒绝的写入未产生任何变更
            loadresponse = await stateStorage.Load();
            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Should().BeEmpty();
            loadresponse.ETag.Should().BeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Should().BeEmpty();
            AssertTState(loadresponse.CommittedState, new TState());

            // 更新元数据中的时间戳，然后使用正确的 ETag 写回
            var now = DateTime.UtcNow;
            var cr = MakeCommitRecords(2, 2);
            var metadata = new TransactionalStateMetaData() { TimeStamp = now, CommitRecords = cr };
            var etag2 = await stateStorage.Store(null, metadata, emptyPendingStates, null, null);
            etag2.Should().NotBeNullOrEmpty();

            var storedResponse = await stateStorage.Load();
            storedResponse.ETag.Should().Be(etag2);
            storedResponse.Metadata.Should().BeEquivalentTo(metadata);
            storedResponse.CommittedSequenceId.Should().Be(0);
            storedResponse.PendingStates.Should().BeEmpty();
            AssertTState(storedResponse.CommittedState, new TState());

            // 更新元数据中的时间戳，然后使用错误的 ETag 写回，必须失败
            var metadata2 = new TransactionalStateMetaData()
            {
                TimeStamp = now.AddSeconds(1),
                CommitRecords = MakeCommitRecords(3, 3)
            };
            Func<Task> storeWithStaleETag = () => stateStorage.Store(
                null,
                metadata2,
                emptyPendingStates,
                null,
                null);
            await storeWithStaleETag.Should().ThrowAsync<Exception>("the ETag is stale");

            // 再次加载并验证被拒绝的写入未产生任何变更
            loadresponse = await stateStorage.Load();
            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.ETag.Should().Be(storedResponse.ETag);
            loadresponse.Metadata.Should().BeEquivalentTo(storedResponse.Metadata);
            loadresponse.CommittedSequenceId.Should().Be(storedResponse.CommittedSequenceId);
            loadresponse.PendingStates.Should().BeEquivalentTo(storedResponse.PendingStates);
            AssertTState(loadresponse.CommittedState, storedResponse.CommittedState);
        }

        private void AssertTState(TState actual, TState expected)
        {
            if (assertConfig == null)
                actual.Should().BeEquivalentTo(expected);
            else
                actual.Should().BeEquivalentTo(expected, assertConfig);
        }

        private static PendingTransactionState<TState> MakePendingState(long seqno, TState val, bool tm)
        {
            var result = new PendingTransactionState<TState>()
            {
                SequenceId = seqno,
                TimeStamp = DateTime.UtcNow,
                TransactionId = Guid.NewGuid().ToString(),
                TransactionManager = tm ? default : MakeParticipantId(),
                State = new TState()
            };
            result.State = val;
            return result;
        }

        private static ParticipantId MakeParticipantId()
        {
            return new ParticipantId(
                                    "tm",
                                    null!,
                                    // (GrainReference) grainFactory.GetGrain<ITransactionTestGrain>(Guid.NewGuid(), TransactionTestConstants.SingleStateTransactionalGrain),
                                    ParticipantId.Role.Resource | ParticipantId.Role.Manager);
        }

        private static Dictionary<Guid, CommitRecord> MakeCommitRecords(int count, int size)
        {
            var result = new Dictionary<Guid, CommitRecord>();
            for (int j = 0; j < size; j++)
            {
                var r = new CommitRecord()
                {
                    Timestamp = DateTime.UtcNow,
                    WriteParticipants = new List<ParticipantId>(),
                };
                for (int i = 0; i < size; i++)
                {
                    r.WriteParticipants.Add(MakeParticipantId());
                }
                result.Add(Guid.NewGuid(), r);
            }
            return result;
        }

        private async Task PrepareOne()
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedState = this.stateFactory(123);
            var pendingstate = MakePendingState(1, expectedState, false);
            _ = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate }, null, null);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(1);
            loadresponse.PendingStates[0].SequenceId.Should().Be(1);
            loadresponse.PendingStates[0].TimeStamp.Should().Be(pendingstate.TimeStamp);
            loadresponse.PendingStates[0].TransactionManager.Should().Be(pendingstate.TransactionManager);
            loadresponse.PendingStates[0].TransactionId.Should().Be(pendingstate.TransactionId);
            AssertTState(loadresponse.PendingStates[0].State, expectedState);
        }

        /// <summary>
        /// 验证确认一个待处理状态会将其提交，并将其从待处理集合中移除。
        /// </summary>
        /// <param name="useTwoSteps">准备和确认是否在不同的存储写入中执行。</param>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task ConfirmOne(bool useTwoSteps)
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedState = this.stateFactory(123);
            var pendingstate = MakePendingState(1, expectedState, false);

            if (useTwoSteps)
            {
                etag = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate }, null, null);
                _ = await stateStorage.Store(etag, metadata, emptyPendingStates, 1, null);
            }
            else
            {
                _ = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate }, 1, null);
            }

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(1);
            loadresponse.PendingStates.Count.Should().Be(0);
            AssertTState(loadresponse.CommittedState, expectedState);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
        }

        /// <summary>
        /// 验证取消一个待处理状态会保留已提交状态，并清空待处理集合。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task CancelOne()
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var pendingstate = MakePendingState(1, this.stateFactory(123), false);

            etag = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate }, null, null);
            _ = await stateStorage.Store(etag, metadata, emptyPendingStates, null, 0);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(0);
            AssertTState(loadresponse.CommittedState, initialstate);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
        }

        /// <summary>
        /// 验证两次准备同一序列号的状态会以最新值替换待处理状态。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task ReplaceOne()
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedState1 = this.stateFactory(123);
            var expectedState2 = this.stateFactory(456);
            var pendingstate1 = MakePendingState(1, expectedState1, false);
            var pendingstate2 = MakePendingState(1, expectedState2, false);

            etag = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate1 }, null, null);
            _ = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate2 }, null, null);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(1);
            loadresponse.PendingStates[0].SequenceId.Should().Be(1);
            loadresponse.PendingStates[0].TimeStamp.Should().Be(pendingstate2.TimeStamp);
            loadresponse.PendingStates[0].TransactionManager.Should().Be(pendingstate2.TransactionManager);
            loadresponse.PendingStates[0].TransactionId.Should().Be(pendingstate2.TransactionId);
            AssertTState(loadresponse.PendingStates[0].State, expectedState2);
        }


        /// <summary>
        /// 验证可以确认一个待处理状态，同时取消紧随其后的待处理状态。
        /// </summary>
        /// <param name="useTwoSteps">确认与取消是否在不同的存储写入中执行。</param>
        /// <param name="reverseOrder">采用两次写入时，是否先存储取消操作，再存储确认操作。</param>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task ConfirmOneAndCancelOne(bool useTwoSteps = false, bool reverseOrder = false)
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedState = this.stateFactory(123);
            var pendingstate1 = MakePendingState(1, expectedState, false);
            var pendingstate2 = MakePendingState(2, this.stateFactory(456), false);

            etag = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate1, pendingstate2 }, null, null);

            if (useTwoSteps)
            {
                if (reverseOrder)
                {
                    etag = await stateStorage.Store(etag, metadata, emptyPendingStates, null, 1);
                    _ = await stateStorage.Store(etag, metadata, emptyPendingStates, 1, null);
                }
                else
                {
                    etag = await stateStorage.Store(etag, metadata, emptyPendingStates, 1, null);
                    _ = await stateStorage.Store(etag, metadata, emptyPendingStates, null, 1);
                }
            }
            else
            {
                _ = await stateStorage.Store(etag, metadata, emptyPendingStates, 1, 1);
            }

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(1);
            loadresponse.PendingStates.Count.Should().Be(0);
            AssertTState(loadresponse.CommittedState, expectedState);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
        }

        /// <summary>
        /// 验证可以在一次存储写入中准备多个连续的状态。
        /// </summary>
        /// <param name="count">要准备的待处理状态数量。</param>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task PrepareMany(int count)
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var pendingstates = new List<PendingTransactionState<TState>>();
            var expectedStates = new List<TState>();
            for (int i = 0; i < count; i++)
            {
                expectedStates.Add(this.stateFactory(i * 1000));
            }

            for (int i = 0; i < count; i++)
            {
                pendingstates.Add(MakePendingState(i + 1, expectedStates[i], false));
            }
            _ = await stateStorage.Store(etag, metadata, pendingstates, null, null);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(count);

            for (int i = 0; i < count; i++)
            {
                loadresponse.PendingStates[i].SequenceId.Should().Be(i + 1);
                loadresponse.PendingStates[i].TimeStamp.Should().Be(pendingstates[i].TimeStamp);
                loadresponse.PendingStates[i].TransactionManager.Should().Be(pendingstates[i].TransactionManager);
                loadresponse.PendingStates[i].TransactionId.Should().Be(pendingstates[i].TransactionId);
                AssertTState(loadresponse.PendingStates[i].State, expectedStates[i]);
            }
        }

        /// <summary>
        /// 验证确认多个待处理状态会提交序列号最大的状态。
        /// </summary>
        /// <param name="count">要准备并确认的待处理状态数量。</param>
        /// <param name="useTwoSteps">准备和确认是否在不同的存储写入中执行。</param>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task ConfirmMany(int count, bool useTwoSteps)
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedStates = new List<TState>();
            for (int i = 0; i < count; i++)
            {
                expectedStates.Add(this.stateFactory(i * 1000));
            }
            var pendingstates = new List<PendingTransactionState<TState>>();
            for (int i = 0; i < count; i++)
            {
                pendingstates.Add(MakePendingState(i + 1, expectedStates[i], false));
            }

            if (useTwoSteps)
            {
                etag = await stateStorage.Store(etag, metadata, pendingstates, null, null);
                _ = await stateStorage.Store(etag, metadata, emptyPendingStates, count, null);
            }
            else
            {
                _ = await stateStorage.Store(etag, metadata, pendingstates, count, null);
            }

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(count);
            loadresponse.PendingStates.Count.Should().Be(0);
            AssertTState(loadresponse.CommittedState, expectedStates[count - 1]);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
        }

        /// <summary>
        /// 验证取消多个待处理状态会保留已提交状态，并清空待处理集合。
        /// </summary>
        /// <param name="count">要准备并取消的待处理状态数量。</param>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task CancelMany(int count)
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedStates = new List<TState>();
            for (int i = 0; i < count; i++)
            {
                expectedStates.Add(this.stateFactory(i * 1000));
            }

            var pendingstates = new List<PendingTransactionState<TState>>();
            for (int i = 0; i < count; i++)
            {
                pendingstates.Add(MakePendingState(i + 1, expectedStates[i], false));
            }

            etag = await stateStorage.Store(etag, metadata, pendingstates, null, null);
            _ = await stateStorage.Store(etag, metadata, emptyPendingStates, null, 0);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(0);
            AssertTState(loadresponse.CommittedState, initialstate);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
        }

        /// <summary>
        /// 验证替换多个待处理序列号对应的状态会持久化替换后的状态。
        /// </summary>
        /// <param name="count">要准备并替换的待处理状态数量。</param>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task ReplaceMany(int count)
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var expectedStates1 = new List<TState>();
            for (int i = 0; i < count; i++)
            {
                expectedStates1.Add(this.stateFactory(i * 1000 + 1));
            }

            var expectedStates2 = new List<TState>();
            for (int i = 0; i < count; i++)
            {
                expectedStates2.Add(this.stateFactory(i * 1000));
            }

            var pendingstates1 = new List<PendingTransactionState<TState>>();
            for (int i = 0; i < count; i++)
            {
                pendingstates1.Add(MakePendingState(i + 1, expectedStates1[i], false));
            }
            var pendingstates2 = new List<PendingTransactionState<TState>>();
            for (int i = 0; i < count; i++)
            {
                pendingstates2.Add(MakePendingState(i + 1, expectedStates2[i], false));
            }

            etag = await stateStorage.Store(etag, metadata, pendingstates1, null, null);
            _ = await stateStorage.Store(etag, metadata, pendingstates2, null, null);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(count);

            for (int i = 0; i < count; i++)
            {
                loadresponse.PendingStates[i].SequenceId.Should().Be(i + 1);
                loadresponse.PendingStates[i].TimeStamp.Should().Be(pendingstates2[i].TimeStamp);
                loadresponse.PendingStates[i].TransactionManager.Should().Be(pendingstates2[i].TransactionManager);
                loadresponse.PendingStates[i].TransactionId.Should().Be(pendingstates2[i].TransactionId);
                AssertTState(loadresponse.PendingStates[i].State, expectedStates2[i]);
            }
        }


        /// <summary>
        /// 验证一次存储更新能够替换待处理状态、添加后续状态并确认前缀部分。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task GrowingBatch()
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var pendingstate1 = MakePendingState(1, this.stateFactory(11), false);
            var pendingstate2 = MakePendingState(2, this.stateFactory(22), false);
            var pendingstate3a = MakePendingState(3, this.stateFactory(333), false);
            var pendingstate4a = MakePendingState(4, this.stateFactory(444), false);
            var pendingstate3b = MakePendingState(3, this.stateFactory(33), false);
            var pendingstate4b = MakePendingState(4, this.stateFactory(44), false);
            var pendingstate5 = MakePendingState(5, this.stateFactory(55), false);

            var expectedState6 = this.stateFactory(66);
            var pendingstate6 = MakePendingState(6, expectedState6, false);
            var expectedState7 = this.stateFactory(77);
            var pendingstate7 = MakePendingState(7, expectedState7, false);
            var expectedState8 = this.stateFactory(88);
            var pendingstate8 = MakePendingState(8, expectedState8, false);


            // 准备 1、2、3a、4a
            etag = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate1, pendingstate2, pendingstate3a, pendingstate4a }, null, null);

            // 替换 3b、4b，准备 5、6、7、8，确认 1、2、3b、4b、5、6
            _ = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate3b, pendingstate4b, pendingstate5, pendingstate6, pendingstate7, pendingstate8 }, 6, null);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(6);
            AssertTState(loadresponse.CommittedState, expectedState6);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(2);
            loadresponse.PendingStates[0].SequenceId.Should().Be(7);
            loadresponse.PendingStates[0].TimeStamp.Should().Be(pendingstate7.TimeStamp);
            loadresponse.PendingStates[0].TransactionManager.Should().Be(pendingstate7.TransactionManager);
            loadresponse.PendingStates[0].TransactionId.Should().Be(pendingstate7.TransactionId);
            AssertTState(loadresponse.PendingStates[0].State, expectedState7);
            loadresponse.PendingStates[1].SequenceId.Should().Be(8);
            loadresponse.PendingStates[1].TimeStamp.Should().Be(pendingstate8.TimeStamp);
            loadresponse.PendingStates[1].TransactionManager.Should().Be(pendingstate8.TransactionManager);
            loadresponse.PendingStates[1].TransactionId.Should().Be(pendingstate8.TransactionId);
            AssertTState(loadresponse.PendingStates[1].State, expectedState8);
        }

        /// <summary>
        /// 验证一次存储更新能够替换并确认前缀部分，同时取消后续待处理状态。
        /// </summary>
        /// <returns>表示存储测试的任务。</returns>
        public virtual async Task ShrinkingBatch()
        {
            var stateStorage = await this.stateStorageFactory();
            var loadresponse = await stateStorage.Load();
            var etag = loadresponse.ETag;
            var metadata = loadresponse.Metadata;
            var initialstate = loadresponse.CommittedState;

            var pendingstate1 = MakePendingState(1, this.stateFactory(11), false);
            var pendingstate2 = MakePendingState(2, this.stateFactory(22), false);
            var pendingstate3a = MakePendingState(3, this.stateFactory(333), false);
            var pendingstate4a = MakePendingState(4, this.stateFactory(444), false);
            var pendingstate5 = MakePendingState(5, this.stateFactory(55), false);
            var pendingstate6 = MakePendingState(6, this.stateFactory(66), false);
            var pendingstate7 = MakePendingState(7, this.stateFactory(77), false);
            var pendingstate8 = MakePendingState(8, this.stateFactory(88), false);
            var expectedState3b = this.stateFactory(33);
            var pendingstate3b = MakePendingState(3, expectedState3b, false);
            var expectedState4b = this.stateFactory(44);
            var pendingstate4b = MakePendingState(4, expectedState4b, false);


            // 准备 1、2、3a、4a、5、6、7、8
            etag = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate1, pendingstate2, pendingstate3a, pendingstate4a, pendingstate5, pendingstate6, pendingstate7, pendingstate8 }, null, null);

            // 替换 3b、4b，确认 1、2、3b，取消 5、6、7、8
            _ = await stateStorage.Store(etag, metadata, new List<PendingTransactionState<TState>>() { pendingstate3b, pendingstate4b }, 3, 4);

            loadresponse = await stateStorage.Load();
            _ = loadresponse.ETag;
            _ = loadresponse.Metadata;

            loadresponse.Should().NotBeNull();
            loadresponse.Metadata.Should().NotBeNull();
            loadresponse.CommittedSequenceId.Should().Be(3);
            AssertTState(loadresponse.CommittedState, expectedState3b);
            loadresponse.Metadata.TimeStamp.Should().Be(default);
            loadresponse.Metadata.CommitRecords.Count.Should().Be(0);
            loadresponse.PendingStates.Count.Should().Be(1);
            loadresponse.PendingStates[0].SequenceId.Should().Be(4);
            loadresponse.PendingStates[0].TimeStamp.Should().Be(pendingstate4b.TimeStamp);
            loadresponse.PendingStates[0].TransactionManager.Should().Be(pendingstate4b.TransactionManager);
            loadresponse.PendingStates[0].TransactionId.Should().Be(pendingstate4b.TransactionId);
            AssertTState(loadresponse.PendingStates[0].State, expectedState4b);
        }

    }
}
