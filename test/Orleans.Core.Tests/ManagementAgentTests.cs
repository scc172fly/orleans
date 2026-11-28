
using Orleans.Runtime;
using Xunit;

namespace UnitTests
{
    /// <summary>
    /// 测试 Orleans 管理和监控组件，尤其是 SystemStatus 枚举。
    /// Orleans 广泛使用 SystemStatus 来跟踪 Silo 及其他运行时组件的生命周期状态。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ManagementAgentTests
    {
        /// <summary>
        /// 测试 SystemStatus 值的相等性实现，确保比较行为正确。
        /// 这对于 Orleans 运行时的状态管理和监控十分重要。
        /// </summary>
        [Fact, TestCategory("Functional"), TestCategory("Management")]
        public void SystemStatusEquals()
        {
            SystemStatus same1 = SystemStatus.Terminated;
            SystemStatus same2 = SystemStatus.Terminated;
            SystemStatus other = SystemStatus.Stopping;

            CheckEquals(same1, same2);
            CheckNotEquals(same1, other);
            CheckNotEquals(same2, other);
        }

        //[Fact]
        //public void CheckEventChannelEnvSetup()
        //{
        //    bool found = ManagementBusConnector.CheckEventChannelEnvSetup();
        //    Console.WriteLine("CheckEventChannelEnvSetup=" + found);
        //}

        private static void CheckEquals(object obj, object other)
        {
            Assert.NotNull(obj);
            Assert.NotNull(other);
            Assert.Equal(obj, other);
            Assert.Equal(other, obj);
            Assert.Equal(obj.GetHashCode(), other.GetHashCode());
            Assert.True(obj.Equals(other));
            Assert.True(other.Equals(obj));
            Assert.True(obj == other);
            Assert.True(other == obj);
            Assert.False(obj != other);
            Assert.False(other != obj);
        }

        private static void CheckNotEquals(object obj, object other)
        {
            Assert.NotNull(obj);
            Assert.NotNull(other);
            Assert.NotEqual(obj, other);
            Assert.NotEqual(other, obj);
            Assert.NotEqual(obj.GetHashCode(), other.GetHashCode());
            Assert.False(obj.Equals(other));
            Assert.False(other.Equals(obj));
            Assert.False(obj == other);
            Assert.False(other == obj);
            Assert.True(obj != other);
            Assert.True(other != obj);
        }
    }
}
