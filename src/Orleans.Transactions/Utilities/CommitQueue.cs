using System;
using System.Collections.Generic;

namespace Orleans.Transactions
{
    /// <summary>
    /// 以时间戳分类的循环缓冲存储交易记录的队列数据结构。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal struct CommitQueue<T>
    {
        private const int DefaultCapacity = 8;
        private TransactionRecord<T>[] _buffer;
        private int _pos;

        public int Count { get; private set; }

        // 索引器可提供文件的读写访问权限。
        public readonly TransactionRecord<T> this[int index]
        {
            get
            {
                if (index < 0 || index > (Count - 1))
                    throw new ArgumentOutOfRangeException(nameof(index));

                return _buffer[(_pos + index) % _buffer.Length];
            }
        }

        public readonly IEnumerable<TransactionRecord<T>> Elements
        {
            get
            {
                if (_buffer != null)
                {
                    for (int i = 0; i < Count; i++)
                        yield return _buffer[(_pos + i) % _buffer.Length];
                }
            }
        }

        public readonly TransactionRecord<T> First => _buffer[_pos];

        public readonly TransactionRecord<T> Last => _buffer[(_pos + Count - 1) % _buffer.Length];

        public void Add(TransactionRecord<T> entry)
        {
            // 保证我们有空间
            if (_buffer == null)
            {
                _buffer = new TransactionRecord<T>[DefaultCapacity];
            }
            else if (Count == _buffer.Length)
            {
                var newBuffer = new TransactionRecord<T>[_buffer.Length * 2];
                Array.Copy(_buffer, _pos, newBuffer, 0, _buffer.Length - _pos);
                Array.Copy(_buffer, 0, newBuffer, _buffer.Length - _pos, _pos);
                _buffer = newBuffer;
                _pos = 0;
            }

            if (Count > 0 && _buffer[(_pos + Count - 1) % _buffer.Length].Timestamp > entry.Timestamp)
                throw new ArgumentException($"elements must be added in timestamp order, but {entry.Timestamp:o} is before {_buffer[(_pos + Count - 1) % _buffer.Length].Timestamp:o}", nameof(entry));

            // 添加元素
            _buffer[(_pos + Count) % _buffer.Length] = entry;
            Count++;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
                _buffer[(_pos + i) % _buffer.Length] = null!;
            Count = 0;
            _pos = 0;
        }

        public void RemoveFromFront(int howMany)
        {
            if (howMany <= 0)
            {
                throw new ArgumentException("Value must be greater than zero", nameof(howMany));
            }

            if (_buffer == null || howMany > Count)
            {
                throw new ArgumentException("cannot remove more elements than are in the queue", nameof(howMany));
            }

            // 清空条目,以便它们能够 geGCd
            for (int i = 0; i < howMany; i++)
                _buffer[(_pos + i) % _buffer.Length] = null!;

            _pos = (_pos + howMany) % _buffer.Length;

            Count -= howMany;
        }

        public void RemoveFromBack(int howMany)
        {
            if (howMany > 0 && (_buffer == null || howMany > Count))
                throw new ArgumentException("cannot remove more elements than are in the queue", nameof(howMany));

            // 清空条目,以便它们能够 geGCd
            for (int i = 0; i < howMany; i++)
                _buffer[(_pos + Count - i - 1) % _buffer.Length] = null!;

            Count -= howMany;
        }

        public readonly int Find(Guid TransactionId, DateTime key)
        {
            // 进行二进搜索
            int left = 0;
            int right = Count;
            while (left < right)
            {
                int mid = (left + right) / 2;
                var record = _buffer[(_pos + mid) % _buffer.Length];
                if (record.Timestamp < key)
                {
                    left = mid + 1;
                    continue;
                }
                else if (record.Timestamp > key)
                {
                    right = mid;
                    continue;
                }
                else if (record.TransactionId == TransactionId)
                {
                    return mid;
                }
                else
                {
                    // 左对左
                    for (int j = mid - 1; j >= left; j--)
                    {
                        record = _buffer[(_pos + j) % _buffer.Length];
                        if (record.TransactionId == TransactionId)
                            return j;
                        if (record.Timestamp != key)
                            break;
                    }
                    // 右对上搜索
                    for (int j = mid + 1; j < right; j++)
                    {
                        record = _buffer[(_pos + j) % _buffer.Length];
                        if (record.TransactionId == TransactionId)
                            return j;
                        if (record.Timestamp != key)
                            break;
                    }
                    return NotFound;
                }
            }

            return NotFound;
        }

        private const int NotFound = -1;
    }
}
