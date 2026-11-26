using System.Diagnostics;

namespace TestExtensions
{
    // 用于测试常量
    public static class TestConstants
    {
        public static readonly TimeSpan InitTimeout =
            Debugger.IsAttached ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(1);
    }
}
