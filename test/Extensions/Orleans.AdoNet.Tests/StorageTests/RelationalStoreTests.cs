using System.Data;
using System.Data.Common;
using Orleans.Tests.SqlUtils;
using UnitTests.General;
using Xunit;

namespace UnitTests.StorageTests.AdoNet
{
    /// <summary>
    /// 流测试所用的数据模型。
    /// </summary>
    public class StreamingTest
    {
        public int Id { get; set; }

        public byte[]? StreamData { get; set; }
    }

    /// <summary>
    /// 关系型存储测试的基类。
    /// </summary>
    public abstract class RelationalStoreTestsBase
    {
        //此超时限制应明显小于 RelationalStorageForTesting.CancellationTestQuery 中定义的限制。
        protected readonly TimeSpan CancellationTestTimeoutLimit = TimeSpan.FromSeconds(5);
        protected readonly TimeSpan StreamCancellationTimeoutLimit = TimeSpan.FromSeconds(15);
        protected const int MiB = 1048576;
        protected const int StreamSizeToBeInsertedInBytes = MiB * 2;
        protected const int NumberOfParallelStreams = 5;

        protected RelationalStoreTestsBase(string adoNetInvariantName)
        {
            RelationalStorageForTesting.CheckPreconditionsOrThrow(adoNetInvariantName);
        }

        protected static Task<bool>[] InsertAndReadStreamsAndCheckMatch(RelationalStorageForTesting sut, int streamSize, int countOfStreams, CancellationToken cancellationToken)
        {
            Assert.SkipWhen(string.IsNullOrEmpty(sut.CurrentConnectionString), "Database was not initialized correctly");

            //并行流式写入和读取三个二进制流。
            var streamChecks = new Task<bool>[countOfStreams];
            for (int i = 0; i < countOfStreams; ++i)
            {
                int streamId = i;
                streamChecks[i] = Task.Run(async () =>
                {
                    var rb = new byte[streamSize];
                    Random.Shared.NextBytes(rb);
                    await InsertIntoDatabaseUsingStream(sut, streamId, rb, cancellationToken);
                    var dataStreamFromTheDb = await ReadFromDatabaseUsingAsyncStream(sut, streamId, cancellationToken);
                    return dataStreamFromTheDb.StreamData.SequenceEqual(rb);
                }, cancellationToken);
            }

            return streamChecks;
        }

        protected static async Task InsertIntoDatabaseUsingStream(RelationalStorageForTesting sut, int streamId, byte[] dataToInsert, CancellationToken cancellationToken)
        {
            Assert.SkipWhen(string.IsNullOrEmpty(sut.CurrentConnectionString), "Database was not initialized correctly");
            //可以直接在此处插入 dataToInsert，但那样就不是流式传输了。
            using (var ms = new MemoryStream(dataToInsert))
            {
                await sut.Storage.ExecuteAsync(sut.StreamTestInsert, command =>
                {
                    var p1 = command.CreateParameter();
                    p1.ParameterName = "Id";
                    p1.Value = streamId;
                    command.Parameters.Add(p1);

                    //MySQL 不支持流式输入，目前也
                    //没有定义自定义流。有关实现思路，可参阅 http://dev.mysql.com/doc/refman/5.7/en/blob.html
                    //中的 BLOB 字符串操作，以及 http://rusanu.com/2010/12/28/download-and-upload-images-from-sql-server-with-asp-net-mvc/
                    //中关于如何定义此类流的内容。
                    var p2 = command.CreateParameter();
                    p2.ParameterName = "StreamData";
                    p2.Value = dataToInsert;
                    p2.DbType = DbType.Binary;
                    p2.Size = dataToInsert.Length;
                    command.Parameters.Add(p2);

                }, CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
            }
        }

        protected static async Task<StreamingTest> ReadFromDatabaseUsingAsyncStream(RelationalStorageForTesting sut, int streamId, CancellationToken cancellationToken)
        {
            Assert.SkipWhen(string.IsNullOrEmpty(sut.CurrentConnectionString), "Database was not initialized correctly");
            return (await sut.Storage.ReadAsync(sut.StreamTestSelect, command =>
            {
                var p = command.CreateParameter();
                p.ParameterName = "streamId";
                p.Value = streamId;
                command.Parameters.Add(p);
            }, async (selector, resultSetCount, cancellationToken) =>
            {
                var streamSelector = (DbDataReader)selector;
                var id = await streamSelector.GetValueAsync<int>("Id", cancellationToken);
                using (var ms = new MemoryStream())
                {
                    using (var downloadStream = streamSelector.GetStream(1, sut.Storage))
                    {
                        await downloadStream.CopyToAsync(ms, cancellationToken);

                        return new StreamingTest { Id = id, StreamData = ms.ToArray() };
                    }
                }
            }, CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false)).Single();
        }

        protected static Task CancellationTokenTest(
            RelationalStorageForTesting sut,
            TimeSpan timeoutLimit,
            CancellationToken testCancellationToken)
        {
            Assert.SkipWhen(string.IsNullOrEmpty(sut.CurrentConnectionString), "Database was not initialized correctly");
            using (var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(testCancellationToken))
            {
                tokenSource.CancelAfter(timeoutLimit);
                try
                {
                    //此处将任务超时限制增加一秒，以补偿延迟。
                    //延迟主要来自底层 ADO.NET 库和数据库。
                    var task = sut.Storage.ReadAsync<int>(sut.CancellationTestQuery, tokenSource.Token);
                    if (!task.Wait(timeoutLimit.Add(TimeSpan.FromSeconds(2)), testCancellationToken))
                    {
                        Assert.Fail(string.Format("Timeout limit {0} ms exceeded.", timeoutLimit.TotalMilliseconds));
                    }
                }
                catch (Exception ex)
                {
                    testCancellationToken.ThrowIfCancellationRequested();

                    //操作被强制取消时可能出现 DbException……
                    //……除非被测提供程序不支持取消操作。
                    //由于测试采用强制同步等待方式，异常被包装在 AggregateException 中，
                    //以强制检查实际取消行为，并排除“自然超时”的影响。
                    var innerException = ex?.InnerException;
                    if (sut.Storage.SupportsCommandCancellation())
                    {
                        //如果在调用数据库之前操作就已经取消，则无论如何都会抛出
                        //OperationCanceledException。
                        Assert.True(innerException is DbException || innerException is OperationCanceledException, $"Unexpected exception: {ex}");
                    }
                    else
                    {
                        Assert.True(innerException is OperationCanceledException, $"Unexpected exception: {ex}");
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}
