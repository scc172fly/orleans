using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// Orleans 多态 Grain 接口测试。
    /// 展示 Grain 如何实现具有继承层次结构的多个接口，
    /// 以及 Grain 引用如何在兼容接口之间转换。
    /// 这支持面向对象的模式，其中 Grain 通过接口继承
    /// 公开不同层次的功能。
    /// </summary>
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Runtime")]
    public class PolymorphicInterfaceTest : HostedTestClusterEnsureDefaultStarted
    {
        public PolymorphicInterfaceTest(DefaultClusterFixture fixture) : base(fixture)
        {
        }

        /// <summary>
        /// 测试基本多态接口方法调用。
        /// 验证实现 IA 接口的 Grain 可通过该接口访问，
        /// 且所有方法均得到正确分派。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_SimpleTest()
        {
            var grainFullName = typeof(PolymorphicTestGrain).FullName;
            IA IARef = this.GrainFactory.GetGrain<IA>(GetRandomGrainId(), grainFullName);
            Assert.Equal("A1", await IARef.A1Method());
            Assert.Equal("A2", await IARef.A2Method());
            Assert.Equal("A3", await IARef.A3Method());
        }

        /// <summary>
        /// 测试通过接口继承层次结构向上转换 Grain 引用。
        /// 验证 Grain 引用可转换为其继承链中的任意接口，
        /// 且转换后所有接口的方法仍然可访问。
        /// 这展示了 Grain 引用中的接口协变。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_UpCastTest()
        {
            var grainFullName = typeof(PolymorphicTestGrain).FullName;
            IC ICRef = this.GrainFactory.GetGrain<IC>(GetRandomGrainId(), grainFullName);
            IA IARef = ICRef; // 转换为多态接口
            Assert.Equal("A1", await IARef.A1Method());
            Assert.Equal("A2", await IARef.A2Method());
            Assert.Equal("A3", await IARef.A3Method());

            IB IBRef = ICRef; // 转换为多态接口
            Assert.Equal("B1", await IBRef.B1Method());
            Assert.Equal("B2", await IBRef.B2Method());
            Assert.Equal("B3", await IBRef.B3Method());

            IF IFRef = this.GrainFactory.GetGrain<IF>(GetRandomGrainId(), grainFullName);

            Assert.Equal("F1", await IFRef.F1Method());
            Assert.Equal("F2", await IFRef.F2Method());
            Assert.Equal("F3", await IFRef.F3Method());

            IE IERef = IFRef; // 转换为多态接口
            Assert.Equal("E1", await IERef.E1Method());
            Assert.Equal("E2", await IERef.E2Method());
            Assert.Equal("E3", await IERef.E3Method());

        }

        /// <summary>
        /// 测试返回多态 Grain 引用的工厂方法。
        /// 验证 Grain 工厂能够使用一种接口类型创建 Grain，
        /// 但以不同的兼容接口类型返回引用，
        /// 从而支持具有接口抽象的工厂模式。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_FactoryMethods()
        {
            var grainFullName = typeof(PolymorphicTestGrain).FullName;
            IC ICRef = this.GrainFactory.GetGrain<IF>(GetRandomGrainId(), grainFullName); // FRef 工厂方法返回指向 ICRef 的多态引用
            Assert.Equal("B2", await ICRef.B2Method());

            IA IARef = this.GrainFactory.GetGrain<ID>(GetRandomGrainId(), grainFullName); // DRef 工厂方法返回指向 IARef 的多态引用
            Assert.Equal("A1", await IARef.A1Method());
        }

        /// <summary>
        /// 测试实现多个接口的服务式 Grain。
        /// 验证单个 Grain 能通过不同接口公开多组功能，
        /// 类似公开多个契约的服务。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_ServiceType()
        {
            var grainFullName = typeof(ServiceType).FullName;
            IServiceType serviceRef = this.GrainFactory.GetGrain<IServiceType>(GetRandomGrainId(), grainFullName);
            Assert.Equal("A1", await serviceRef.A1Method());
            Assert.Equal("A2", await serviceRef.A2Method());
            Assert.Equal("A3", await serviceRef.A3Method());
            Assert.Equal("B1", await serviceRef.B1Method());
            Assert.Equal("B2", await serviceRef.B2Method());
            Assert.Equal("B3", await serviceRef.B3Method());
        }

        /// <summary>
        /// 测试菱形继承场景中的歧义方法解析。
        /// 当多个接口定义相同的方法签名时，验证
        /// 转换为指定接口能遵循 C# 接口继承规则，
        /// 正确解析所调用的实现。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_InheritedMethodAmbiguity()
        {
            // 测试涉及重复方法名的接口继承层次结构，需要通过类型转换消除歧义。
            var grainFullName = typeof(ServiceType).FullName;
            var serviceRef = this.GrainFactory.GetGrain<IServiceType>(GetRandomGrainId(), grainFullName);
            var ia = (IA)serviceRef;
            var ib = (IB)serviceRef;
            var ic = (IC)serviceRef;
            Assert.Equal("IA", await ia.CommonMethod());
            Assert.Equal("IB", await ib.CommonMethod());
            Assert.Equal("IC", await ic.CommonMethod());
        }

        /// <summary>
        /// 针对复杂多态 Grain 场景的综合测试。
        /// 测试实现多个接口层次结构的派生服务类型，
        /// 验证以下内容：
        /// - 继承链中的所有接口均可访问
        /// - 在受支持的情况下，不相关接口之间可以横向转换
        /// - 派生接口方法与基接口方法均可使用
        /// 这汇总了所有多态 Grain 引用用例。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic__DerivedServiceType()
        {
            var grainFullName = typeof(DerivedServiceType).FullName;
            IDerivedServiceType derivedRef = this.GrainFactory.GetGrain<IDerivedServiceType>(GetRandomGrainId(), grainFullName);

            IA IARef = derivedRef;
            Assert.Equal("A1", await IARef.A1Method());
            Assert.Equal("A2", await IARef.A2Method());
            Assert.Equal("A3", await IARef.A3Method());


            IB IBRef = (IB)IARef; // 这可能导致无效类型转换异常，但此处不应发生，因为我们预先知道 DerivedServiceType 实现了该接口
            Assert.Equal("B1", await IBRef.B1Method());
            Assert.Equal("B2", await IBRef.B2Method());
            Assert.Equal("B3", await IBRef.B3Method());

            IF IFRef = (IF)IBRef;
            Assert.Equal("F1", await IFRef.F1Method());
            Assert.Equal("F2", await IFRef.F2Method());
            Assert.Equal("F3", await IFRef.F3Method());

            IE IERef = (IE)IFRef;
            Assert.Equal("E1", await IERef.E1Method());
            Assert.Equal("E2", await IERef.E2Method());
            Assert.Equal("E3", await IERef.E3Method());

            IH IHRef = derivedRef;
            Assert.Equal("H1", await IHRef.H1Method());
            Assert.Equal("H2", await IHRef.H2Method());
            Assert.Equal("H3", await IHRef.H3Method());

            IServiceType serviceTypeRef = derivedRef; // 向上转换指针引用
            Assert.Equal("ServiceTypeMethod1", await serviceTypeRef.ServiceTypeMethod1());
            Assert.Equal("ServiceTypeMethod2", await serviceTypeRef.ServiceTypeMethod2());
            Assert.Equal("ServiceTypeMethod3", await serviceTypeRef.ServiceTypeMethod3());

            Assert.Equal("DerivedServiceTypeMethod1", await derivedRef.DerivedServiceTypeMethod1());
        }
    }
}
