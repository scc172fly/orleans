
using Orleans.Runtime;
using Xunit;

namespace UnitTests
{
    /// <summary>
    /// Orleans 管理和监测组成部分的测试,特别是 SystemStatus 点数。
    /// SystemStatus 整个 Orleans 跟踪 silos 和其他运行时部件。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class ManagementAgentTests
    {
        /// <summary>
        /// 测试 SystemStatus 值的平等落实情况,以确保适当的比较行为。
        /// 这对于国家在整个 Orleans 运行期间的管理和监测十分重要。
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

        //[真
        //公共无效 CheckEventChannelEnvSetup()
        //{
        //    找到布层 = ManagementBusConnector.CheckEventChannelEnvSetup();
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
