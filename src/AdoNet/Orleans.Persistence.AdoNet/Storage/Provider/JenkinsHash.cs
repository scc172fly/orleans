using System;

namespace Orleans.Storage
{
    // 基于 http://home.comcast.net/~bretm/hash/7.html 中的版本，而该版本又基于
    // http://burtleburtle.net/bob/hash/evahash.html 中的版本。
    // 注意，我们只使用 Orleans 团队编写的、接受三个 ulong 参数的版本。
    // 从 Orleans v3.7.2 恢复的实现：https://github.com/dotnet/orleans/blob/b24e446abfd883f0e4ed614f5267eaa3331548dc/src/Orleans.Core.Abstractions/IDs/JenkinsHash.cs，
    // 经过精简及少量优化。
    internal static class JenkinsHash
    {
        private static void Mix(ref uint aa, ref uint bb, ref uint cc)
        {
            uint a = aa;
            uint b = bb;
            uint c = cc;

            a -= b; a -= c; a ^= (c >> 13);
            b -= c; b -= a; b ^= (a << 8);
            c -= a; c -= b; c ^= (b >> 13);
            a -= b; a -= c; a ^= (c >> 12);
            b -= c; b -= a; b ^= (a << 16);
            c -= a; c -= b; c ^= (b >> 5);
            a -= b; a -= c; a ^= (c >> 3);
            b -= c; b -= a; b ^= (a << 10);
            c -= a; c -= b; c ^= (b >> 15);

            aa = a;
            bb = b;
            cc = c;
        }

        // 这是 Jenkins 哈希的参考实现。
        public static uint ComputeHash(ReadOnlySpan<byte> data)
        {
            int len = data.Length;
            uint a = 0x9e3779b9;
            uint b = a;
            uint c = 0;
            int i = 0;

            while (i <= len - 12)
            {
                a += (uint)data[i++] |
                    ((uint)data[i++] << 8) |
                    ((uint)data[i++] << 16) |
                    ((uint)data[i++] << 24);
                b += (uint)data[i++] |
                    ((uint)data[i++] << 8) |
                    ((uint)data[i++] << 16) |
                    ((uint)data[i++] << 24);
                c += (uint)data[i++] |
                    ((uint)data[i++] << 8) |
                    ((uint)data[i++] << 16) |
                    ((uint)data[i++] << 24);
                Mix(ref a, ref b, ref c);
            }
            c += (uint)len;
            if (i < len)
                a += data[i++];
            if (i < len)
                a += (uint)data[i++] << 8;
            if (i < len)
                a += (uint)data[i++] << 16;
            if (i < len)
                a += (uint)data[i++] << 24;
            if (i < len)
                b += (uint)data[i++];
            if (i < len)
                b += (uint)data[i++] << 8;
            if (i < len)
                b += (uint)data[i++] << 16;
            if (i < len)
                b += (uint)data[i++] << 24;
            if (i < len)
                c += (uint)data[i++] << 8;
            if (i < len)
                c += (uint)data[i++] << 16;
            if (i < len)
                c += (uint)data[i++] << 24;
            Mix(ref a, ref b, ref c);
            return c;
        }
    }
}
