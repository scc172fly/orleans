using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#if CLUSTERING_ADONET
namespace Orleans.Clustering.AdoNet.Storage
#elif PERSISTENCE_ADONET
namespace Orleans.Persistence.AdoNet.Storage
#elif REMINDERS_ADONET
namespace Orleans.Reminders.AdoNet.Storage
#elif STREAMING_ADONET
namespace Orleans.Streaming.AdoNet.Storage
#elif GRAINDIRECTORY_ADONET
namespace Orleans.GrainDirectory.AdoNet.Storage
#elif TESTER_SQLUTILS
namespace Orleans.Tests.SqlUtils
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 适应 <see cref="DbDataReader.GetBytes(int, long, byte[], int, int)"/>
    /// 在一个块状的只读串流中。
    /// </summary>
    internal class OrleansRelationalDownloadStream : Stream
    {
        /// <summary>
        /// 缓存任务, 好像有多轮读数, 可能
        /// 读取的字节相同。这可以省去一个分配。
        /// </summary>
        private Task<int>? _lastTask;

        /// <summary>
        /// 从数据库阅读的读者。
        /// </summary>
        private DbDataReader _reader;

        /// <summary>
        /// 整个流中的位置。
        /// </summary>
        private long _position;

        /// <summary>
        /// 栏列的顺序, 读取 。
        /// </summary>
        private readonly int _ordinal;

        /// <summary>
        /// 列中的字节总数。
        /// </summary>
        private readonly long _totalBytes;

        /// <summary>
        /// 初始化一个新的 <see cref="OrleansRelationalDownloadStream"/> 实例。
        /// </summary>
        /// <param name="reader">从数据库阅读的读者。</param>
        /// <param name="ordinal">栏列的顺序, 读取 。</param>
        public OrleansRelationalDownloadStream(DbDataReader reader, int ordinal)
        {
            _reader = reader;
            _ordinal = ordinal;

            //此返回正弦值指向的列的总长度。
            _totalBytes = reader.GetBytes(ordinal, 0, null, 0, 0);
        }

        /// <inheritdoc/>
        public override bool CanRead => _reader != null && (!_reader.IsClosed);

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanTimeout => true;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => _totalBytes;

        /// <inheritdoc/>
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush() => throw new NotSupportedException();

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            //如果参数无效, 则会用相同的参数名称丢弃此参数 。
            ValidateReadParameters(buffer, offset, count);

            try
            {
                int length = Math.Min(count, (int)(_totalBytes - _position));
                long bytesRead = 0;
                if (length > 0)
                {
                    bytesRead = _reader.GetBytes(_ordinal, _position, buffer, offset, length);
                    _position += bytesRead;
                }

                return (int)bytesRead;
            }
            catch (DbException dex)
            {
                //扔一条流的除生物外的物品是不行的
                throw new IOException(dex.Message, dex);
            }
        }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            //如果参数无效, 则会用相同的参数名称丢弃此参数 。
            ValidateReadParameters(buffer, offset, count);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(cancellationToken);
            }

            try
            {
                //保存最后一个使用的任务, 以避免当读取字节数时出现一个分配
                //可能重复相同次数。
                int bytesRead = Read(buffer, offset, count);
#pragma warning disable CA1849 // _lastTask is created only by Task.FromResult and is already successfully completed.
                var ret = _lastTask != null && bytesRead == _lastTask.Result ? _lastTask : (_lastTask = Task.FromResult(bytesRead));
#pragma warning restore CA1849

                return ret;
            }
            catch (Exception e)
            {
                return Task.FromException<int>(e);
            }
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader = null!;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// 检查通过到 ReadAsync() 或 Read() 的参数是否有效。
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        private static void ValidateReadParameters(byte[] buffer, long offset, long count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            if (checked(offset + count) > buffer.Length)
            {
                throw new ArgumentException("Invalid offset length");
            }
        }
    }
}

#nullable restore
