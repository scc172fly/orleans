using Orleans.Runtime;
using Orleans.TestingHost;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit.Consistency
{
    /// <summary>
    /// 执行随机事务序列，并验证观测到的历史记录是否具有串行一致性。
    /// </summary>
    public class ConsistencyTestHarness
    {
        private readonly ConsistencyTestOptions options;
        private Action<string> output = _ => { };

        private readonly Dictionary<int,       // Grain
                          SortedDictionary<int, // SeqNo
                          Dictionary<string,    // WriterTx
                          HashSet<string>>>>    // ReaderTx
            tuples;

        private readonly HashSet<string> succeeded;
        private readonly HashSet<string> aborted;
        private readonly Dictionary<string, string> indoubt;
        private bool timeoutsOccurred;
        private readonly bool tolerateUnknownExceptions;
        private readonly IGrainFactory grainFactory;

        private readonly Dictionary<string, HashSet<string>> orderEdges = new Dictionary<string, HashSet<string>>();
        private readonly Dictionary<string, bool> marks = new Dictionary<string, bool>();


        /// <summary>
        /// 初始化 <see cref="ConsistencyTestHarness"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于创建一致性测试 Grain 的 Grain 工厂。</param>
        /// <param name="numGrains">参与测试的 Grain 数量。</param>
        /// <param name="seed">用于生成可复现测试操作的随机种子。</param>
        /// <param name="avoidDeadlocks">是否对嵌套 Grain 调用排序以避免死锁。</param>
        /// <param name="avoidTimeouts">是否在事务超时前停止调度嵌套调用。</param>
        /// <param name="readWrite">选择读操作和写操作的方式。</param>
        /// <param name="tolerateUnknownExceptions">测试场景是否预期出现无法分类的事务结果。</param>
        public ConsistencyTestHarness(
            IGrainFactory grainFactory,
            int numGrains,
            int seed,
            bool avoidDeadlocks,
            bool avoidTimeouts,
            ReadWriteDetermination readWrite,
            bool tolerateUnknownExceptions)
        {
            this.grainFactory = grainFactory;

            numGrains.Should().BeLessThan(ConsistencyTestOptions.MaxGrains);
            this.options = new ConsistencyTestOptions()
            {
                AvoidDeadlocks = avoidDeadlocks,
                ReadWrite = readWrite,
                MaxDepth = 5,
                NumGrains = numGrains,
                RandomSeed = seed,
                AvoidTimeouts = avoidTimeouts,
                GrainOffset = (DateTime.UtcNow.Ticks & 0xFFFFFFFF) * ConsistencyTestOptions.MaxGrains,
            };

            this.tuples = new Dictionary<int, SortedDictionary<int, Dictionary<string, HashSet<string>>>>();
            this.succeeded = new HashSet<string>();
            this.aborted = new HashSet<string>();
            this.indoubt = new Dictionary<string, string>();

            // 确定最终需要检查的内容
            this.tolerateUnknownExceptions = tolerateUnknownExceptions;
        }

        /// <summary>
        /// 分配给每个 Grain 初始状态的合成事务标识符。
        /// </summary>
        public const string InitialTx = "initial";

        /// <summary>
        /// 获取记录为已中止的事务数量。
        /// </summary>
        public int NumAborted => aborted.Count;

        internal void RecordSucceeded(params Observation[] result)
        {
            if (result.Length == 0)
            {
                return;
            }

            var id = result[0].ExecutingTx;

            foreach (var tuple in result)
            {
                tuple.ExecutingTx.Should().BeEquivalentTo(id);
            }

            lock (succeeded)
            {
                succeeded.Add(id);
            }

            foreach (var tuple in result)
            {
                RecordObservation(tuple);
            }
        }

        internal void RecordObservation(Observation tuple)
        {
            lock (tuples)
            {
                if (!tuples.TryGetValue(tuple.Grain, out var versions))
                {
                    tuples.Add(tuple.Grain, versions = new SortedDictionary<int, Dictionary<string, HashSet<string>>>());
                }

                if (!versions.TryGetValue(tuple.SeqNo, out var writers))
                {
                    versions.Add(tuple.SeqNo, writers = new Dictionary<string, HashSet<string>>());
                }

                if (!writers.TryGetValue(tuple.WriterTx, out var readers))
                {
                    writers.Add(tuple.WriterTx, readers = new HashSet<string>());
                }

                readers.Add(tuple.ExecutingTx);
            }
        }

        internal void RecordAborted(string transactionId)
        {
            lock (aborted)
            {
                aborted.Add(transactionId);
            }
        }

        internal void RecordInDoubt(string transactionId, string message)
        {
            lock (indoubt)
            {
                indoubt.Add(transactionId, message);
            }
        }

        internal void RecordTimeout() => timeoutsOccurred = true;

        /// <summary>
        /// 为一个测试分区执行确定性的随机事务序列。
        /// </summary>
        /// <param name="partition">用于派生随机序列的分区索引。</param>
        /// <param name="count">要执行的事务数量。</param>
        /// <param name="grainFactory">用于创建一致性测试 Grain 引用的 Grain 工厂。</param>
        /// <param name="output">用于输出测试诊断信息的回调。</param>
        /// <returns>表示事务序列的任务。</returns>
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "A seeded PRNG selects test-grain targets for consistency workloads.")]
        public async Task RunRandomTransactionSequence(int partition, int count, IGrainFactory grainFactory, Action<string> output)
        {
            ArgumentNullException.ThrowIfNull(grainFactory);
            ArgumentNullException.ThrowIfNull(output);

            this.output = output;
            var localRandom = new Random(options.RandomSeed + partition);

            for (int i = 0; i < count; i++)
            {
                var target = localRandom.Next(options.NumGrains);
                output($"({partition},{i}) g{target}");

                try
                {
                    var targetgrain = grainFactory.GetGrain<IConsistencyTestGrain>(options.GrainOffset + target);
                    var stopAfter = options.AvoidTimeouts ? DateTime.UtcNow + TimeSpan.FromSeconds(22) : DateTime.MaxValue;
                    var result = await targetgrain.Run(options, 0, $"({partition},{i})", options.NumGrains, stopAfter);

                    if (result.Length > 0)
                    {
                        output($"{partition}.{i} g{target} -> {result.Length} tuples");
                        RecordSucceeded(result);
                    }

                }
                catch (OrleansTransactionAbortedException e)
                {
                    output($"{partition}.{i} g{target} -> aborted {e.GetType().Name} {e.InnerException} {e.TransactionId}");
                    RecordAborted(e.TransactionId);
                }
                catch (OrleansTransactionInDoubtException f)
                {
                    output($"{partition}.{i} g{target} -> in doubt {f.TransactionId}");
                    RecordInDoubt(f.TransactionId, f.Message);
                }
                catch (System.TimeoutException)
                {
                    output($"{partition}.{i} g{target} -> timeout");
                    RecordTimeout();
                }
                catch (OrleansException o)
                {
                    if (o.InnerException is RandomlyInjectedStorageException)
                        output($"{partition}.{i} g{target} -> injected fault");
                    else
                        throw;
                }
            }
        }

        /// <summary>
        /// 验证记录历史中的版本顺序、事务结果及可串行化性。
        /// </summary>
        /// <param name="tolerateGenericTimeouts">是否允许因记录到的一般超时而出现不完整的历史记录。</param>
        /// <param name="tolerateUnknownExceptions">是否接受提交结果不确定的失败。</param>
        public void CheckConsistency(bool tolerateGenericTimeouts = false, bool tolerateUnknownExceptions = false)
        {
            orderEdges.Clear();
            marks.Clear();

            foreach (var grainKvp in tuples)
            {
                CheckGrainConsistency(grainKvp.Key, grainKvp.Value);
            }

            // 通过深度优先搜索在先后顺序关系图中寻找环（即违反可串行化性）。
            DFS();

            // 报告未知异常
            if (!tolerateUnknownExceptions)
            {
                ReportInDoubtCommitFailures();
            }

            // 报告超时异常
            if (!tolerateGenericTimeouts && timeoutsOccurred)
            {
                true.Should().BeFalse($"generic timeout exception caught");
            }
        }

        private void CheckGrainConsistency(
            int grain,
            SortedDictionary<int, Dictionary<string, HashSet<string>>> versions)
        {
            var pos = 0;
            var readersOfPreviousVersion = new HashSet<string>();

            foreach (var seqnoKvp in versions)
            {
                var seqno = seqnoKvp.Key;

                if (pos++ != seqno && indoubt.Count == 0 && !timeoutsOccurred)
                {
                    Fail(grain, versions, $"g{grain} is missing version v{pos - 1}, found v{seqno} instead");
                }

                var writers = seqnoKvp.Value;
                if (writers.Count != 1)
                {
                    Fail(grain, versions, $"g{grain} v{seqno} has multiple writers {string.Join(",", writers.Keys)}");
                }

                var writer = writers.First().Key;
                var readers = writers.First().Value;

                CheckWriter(grain, seqno, writer, readers, versions);
                AddPreviousVersionEdges(readersOfPreviousVersion, writer);
                AddCurrentVersionEdges(grain, seqno, writer, readers, versions);
                readersOfPreviousVersion = readers;
            }
        }

        private void CheckWriter(
            int grain,
            int seqno,
            string writer,
            HashSet<string> readers,
            SortedDictionary<int, Dictionary<string, HashSet<string>>> versions)
        {
            if (seqno == 0)
            {
                if (writer != InitialTx)
                {
                    Fail(grain, versions, $"g{grain} v{seqno} not written by {InitialTx}");
                }

                return;
            }

            if (aborted.Contains(writer))
            {
                Fail(grain, versions, $"g{grain} v{seqno} written by aborted transaction {writer}");
            }

            if (!timeoutsOccurred && !(succeeded.Contains(writer) || indoubt.ContainsKey(writer)))
            {
                Fail(grain, versions, $"g{grain} v{seqno} written by unknown transaction {writer}");
            }

            if (indoubt.Count == 0 && !timeoutsOccurred && !readers.Contains(writer))
            {
                Fail(grain, versions, $"g{grain} v{seqno} writer {writer} missing");
            }
        }

        private void AddPreviousVersionEdges(HashSet<string> readersOfPreviousVersion, string writer)
        {
            foreach (var reader in readersOfPreviousVersion)
            {
                if (reader == writer)
                {
                    continue;
                }

                if (!orderEdges.TryGetValue(reader, out var readEdges))
                {
                    orderEdges[reader] = readEdges = new HashSet<string>();
                }

                readEdges.Add(writer);
            }
        }

        private void AddCurrentVersionEdges(
            int grain,
            int seqno,
            string writer,
            HashSet<string> readers,
            SortedDictionary<int, Dictionary<string, HashSet<string>>> versions)
        {
            if (!orderEdges.TryGetValue(writer, out var writeEdges))
            {
                orderEdges[writer] = writeEdges = new HashSet<string>();
            }

            foreach (var reader in readers)
            {
                if (reader == writer)
                {
                    continue;
                }

                if (!succeeded.Contains(reader))
                {
                    Fail(grain, versions, $"g{grain} v{seqno} read by aborted transaction {reader}");
                }

                writeEdges.Add(reader);
            }
        }

        private void Fail(
            int grain,
            SortedDictionary<int, Dictionary<string, HashSet<string>>> versions,
            string message)
        {
            foreach (var version in versions)
            {
                foreach (var writer in version.Value)
                {
                    foreach (var reader in writer.Value)
                    {
                        output($"g{grain} v{version.Key} w:{writer.Key} a:{reader}");
                    }
                }
            }

            true.Should().BeFalse(message);
        }

        private void ReportInDoubtCommitFailures()
        {
            foreach (var transaction in indoubt)
            {
                if (transaction.Value.Contains("failure during transaction commit", StringComparison.Ordinal))
                {
                    true.Should().BeFalse($"exception during commit {transaction.Key} {transaction.Value}");
                }
            }
        }


        private void DFS()
        {
            foreach (var kvp in orderEdges)
                if (!marks.ContainsKey(kvp.Key))
                {
                    var cycleFound = Visit(kvp.Key, kvp.Value);
                    cycleFound.Should().BeFalse($"found serializability violation");
                }
        }

        private bool Visit(string node, HashSet<string> edges)
        {
            if (marks.TryGetValue(node, out var mark))
            {
                if (mark)
                {
                    return false;
                }
                else
                {
                    output($"!!! CYCLE FOUND:");
                    output($"{node}");
                    return true;
                }
            }
            else
            {
                marks[node] = false;
                foreach (var n in edges)
                    if (orderEdges.TryGetValue(n, out var edges2))
                    {
                        if (Visit(n, edges2))
                        {
                            output($"{node}");
                            return true;
                        }
                    }
                marks[node] = true;
                return false;
            }
        }
    }
}
