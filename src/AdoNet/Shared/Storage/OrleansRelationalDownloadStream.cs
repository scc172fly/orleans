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
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 将 <see cref="DbDataReader.GetBytes(int, long, byte[], int, int)"/> 适配
    /// 为分块读取的只读流。
    /// </summary>
    internal class OrleansRelationalDownloadStream : Stream
    {
        /// <summary>
        /// 缓存的任务。如果存在多轮读取操作，则很可能
        /// 每轮读取的字节数相同。这样可以减少一次内存分配。
        /// </summary>
        private Task<int>? _lastTask;

        /// <summary>
        /// 用于从数据库读取的读取器。
        /// </summary>
        private DbDataReader _reader;

        /// <summary>
        /// 在整个流中的位置。
        /// </summary>
        private long _position;

        /// <summary>
        /// 要读取的列序号。
        /// </summary>
        private readonly int _ordinal;

        /// <summary>
        /// 列中的总字节数。
        /// </summary>
        private readonly long _totalBytes;

        /// <summary>
        /// 初始化新的 <see cref="OrleansRelationalDownloadStream"/> 实例。
        /// </summary>
        /// <param name="reader">用于从数据库读取的读取器。</param>
        /// <param name="ordinal">要读取的列序号。</param>
        public OrleansRelationalDownloadStream(DbDataReader reader, int ordinal)
        {
            _reader = reader;
            _ordinal = ordinal;

            // 返回指定序号对应列的总长度。
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
            // 如果参数无效，将使用相同的参数名称抛出异常。
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
                // 不应从 Stream 抛出非 IOException 类型的异常。
                throw new IOException(dex.Message, dex);
            }
        }

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // 如果参数无效，将使用相同的参数名称抛出异常。
            ValidateReadParameters(buffer, offset, count);

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<int>(cancellationToken);
            }

            try
            {
                // 保存最近使用的任务，以便在多次读取的字节数
                // 很可能相同时减少一次内存分配。
                int bytesRead = Read(buffer, offset, count);
#pragma warning disable CA1849 // _lastTask 仅通过 Task.FromResult 创建，因此已经成功完成。
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
        /// 检查传入 ReadAsync() 或 Read() 的参数是否有效。
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
