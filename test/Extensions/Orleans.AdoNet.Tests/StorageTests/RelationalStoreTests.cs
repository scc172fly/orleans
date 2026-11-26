using System.Data;
using System.Data.Common;
using Orleans.Tests.SqlUtils;
using UnitTests.General;
using Xunit;

namespace UnitTests.StorageTests.AdoNet
{
    /// <summary>
    /// 溪流测试的试验数据模型。
    /// </summary>
    public class StreamingTest
    {
        public int Id { get; set; }

        public byte[]? StreamData { get; set; }
    }

    /// <summary>
    /// 关系储存测试的基础等级。
    /// </summary>
    public abstract class RelationalStoreTestsBase
    {
        //这一超时期限应明显低于 RelationalStorageForTesting.CancellationTestQuery中定义的限值。 
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

            //平行流进并蒸发三条二元溪流
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
            //dataToInsert 可以直接插入到此处,但不会流出。
            using (var ms = new MemoryStream(dataToInsert))
            {
                await sut.Storage.ExecuteAsync(sut.StreamTestInsert, command =>
                {
                    var p1 = command.CreateParameter();
                    p1.ParameterName = "Id";
                    p1.Value = streamId;
                    command.Parameters.Add(p1);

                    //MySQL 不支持流流流的进出和暂时支持流流
                    //并不是定义的自定义流。对于想法,请参见 http://dev.mysql.com/doc/refman/5.7/en/blob.html
                    //用于 blobs 和 http://rusanu.com/2010/12/28/download-and-upload-images-from-sql-server-with-asp-net-mvc/字符串操作的字符串操作
                    //如何去定义一个。
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
                    //在此,任务超时期限后增加一秒钟,以说明延误的原因。
                    //延误主要发生在基本 ADO.NET 图书馆和数据库。
                    var task = sut.Storage.ReadAsync<int>(sut.CancellationTestQuery, tokenSource.Token);
                    if (!task.Wait(timeoutLimit.Add(TimeSpan.FromSeconds(2)), testCancellationToken))
                    {
                        Assert.Fail(string.Format("Timeout limit {0} ms exceeded.", timeoutLimit.TotalMilliseconds));
                    }
                }
                catch (Exception ex)
                {
                    testCancellationToken.ThrowIfCancellationRequested();

                    //由于行动被强行取消, 可能会有 DbException ...
                    //除非这是对不支持取消的供应商的测试
                    //由于硬同步的测试安排,例外被包嵌到 AggregrateException
                    //等待实际取消检查后强制取消,并删除“自然超时”原因。
                    var innerException = ex?.InnerException;
                    if (sut.Storage.SupportsCommandCancellation())
                    {
                        //如果在数据库呼叫前已经取消操作,则使用 OperationCancelledException
                        //在任何情况下,都必被抛下。
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
