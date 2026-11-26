using System;
using System.Buffers;
using System.Text;

namespace Orleans.Storage
{
    /// <summary>
    /// Orleans v3 兼容的散列执行只对字符串 grain 金钥代号 。
    /// </summary>
    internal class Orleans3CompatibleStringKeyHasher : IHasher
    {
        private readonly Orleans3CompatibleHasher _innerHasher;
        private readonly string _grainType;

        public Orleans3CompatibleStringKeyHasher(Orleans3CompatibleHasher innerHasher, string grainType)
        {
            _innerHasher = innerHasher;
            _grainType = grainType;
        }

        /// <summary>
        /// <see cref="IHasher.Description"/>
        /// </summary>
        public string Description { get; } = $"Orleans v3 hash function ({nameof(JenkinsHash)}).";

        /// <summary>
        /// <see cref="IHasher.Hash(byte[])"/>.
        /// </summary>
        public int Hash(byte[] data)
        {
            // Orleans v3 将只用字符串键作为扩展名的整数键(AdoGrainKey.IsLongKey =真),
            // 因此数据必须扩展至只使用字符串 grain 键。
            // 但 AdoNetGrainStorage 执行也使用这种代码:
            //    ...
            //    var grainIdHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState).Hash(grainId.GetHashBytes());
            //    var grainTypeHash = HashPicker.PickHasher(serviceId, this.name, baseGrainType, grainReference, grainState).Hash(Encoding.UTF8.GetBytes(baseGrainType));
            //    ...
            // 两个调用都具有相同的PickHasher 参数,因此我们需要分析数据内容以区分这些情况。
            // 字符串键是否等于 grain 类型名称, 它不字词, 但我们认为这个边缘大小写非常罕见 。

            if (IsGrainTypeName(data))
                return _innerHasher.Hash(data);

            var extendedLength = data.Length + 8;

            const int maxOnStack = 256;
            byte[]? rentedBuffer = null;

            // 假设以下代码从不扔,所以呼叫 ArrayPool.Return 而不尝试/最后阻止 JIT 优化

            var buffer = extendedLength > maxOnStack
                ? (rentedBuffer = ArrayPool<byte>.Shared.Rent(extendedLength)).AsSpan()
                : stackalloc byte[maxOnStack];

            buffer = buffer[..extendedLength];

            data.AsSpan().CopyTo(buffer);
            // 缓冲区中可能含有任意数据, 在“ 扩展” 段设置零
            buffer[data.Length..].Clear();

            var hash = _innerHasher.Hash(buffer);

            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);

            return hash;
        }

        private bool IsGrainTypeName(byte[] data)
        {
            // 字节/ 每字符至少 1 字节
            if (data.Length < _grainType.Length)
                return false;

            var grainTypeByteCount = Encoding.UTF8.GetByteCount(_grainType);
            if (grainTypeByteCount != data.Length)
                return false;

            const int maxOnStack = 256;
            byte[]? rentedBuffer = null;

            // 假设以下代码从不扔,所以呼叫 ArrayPool.Return 而不尝试/最后阻止 JIT 优化

            var buffer = grainTypeByteCount > maxOnStack
                ? (rentedBuffer = ArrayPool<byte>.Shared.Rent(grainTypeByteCount)).AsSpan()
                : stackalloc byte[maxOnStack];

            buffer = buffer[..grainTypeByteCount];

            var bytesWritten = Encoding.UTF8.GetBytes(_grainType, buffer);
            var isGrainType = buffer[..bytesWritten].SequenceEqual(data);
            if (rentedBuffer is not null)
                ArrayPool<byte>.Shared.Return(rentedBuffer);

            return isGrainType;
        }
    }
}
