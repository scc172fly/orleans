using TestExtensions;
using UnitTests.GrainInterfaces;
using UnitTests.Grains;
using Xunit;

namespace DefaultCluster.Tests.General
{
    /// <summary>
    /// 多形态试验 grain 接口 Orleans.
    /// 演示 grains 如何能实施多个与继承的接口
    /// 等级,以及如何在兼容的界面之间绘制 grain 引用。
    /// 允许在 grains 暴露不同级别时以对象为导向的模式
    /// 通过互动继承发挥功能。
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
        /// 测试基本多形态界面方法调用。
        /// 证明可访问 grain 实施接口 IA
        /// 通过该接口,所有方法均妥善发送。
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
        /// 通过界面继承等级,测试对 grain 引用进行升级。
        /// 证明a grain 中的任何界面可以投入
        /// 继承链,所有界面的方法仍然可以使用
        /// 铸件后。在 grain 引用中显示界面共变量。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_UpCastTest()
        {
            var grainFullName = typeof(PolymorphicTestGrain).FullName;
            IC ICRef = this.GrainFactory.GetGrain<IC>(GetRandomGrainId(), grainFullName);
            IA IARef = ICRef; // 绑绑到多色界面
            Assert.Equal("A1", await IARef.A1Method());
            Assert.Equal("A2", await IARef.A2Method());
            Assert.Equal("A3", await IARef.A3Method());

            IB IBRef = ICRef; // 绑绑到多色界面
            Assert.Equal("B1", await IBRef.B1Method());
            Assert.Equal("B2", await IBRef.B2Method());
            Assert.Equal("B3", await IBRef.B3Method());

            IF IFRef = this.GrainFactory.GetGrain<IF>(GetRandomGrainId(), grainFullName);

            Assert.Equal("F1", await IFRef.F1Method());
            Assert.Equal("F2", await IFRef.F2Method());
            Assert.Equal("F3", await IFRef.F3Method());

            IE IERef = IFRef; // 绑绑到多色界面
            Assert.Equal("E1", await IERef.E1Method());
            Assert.Equal("E2", await IERef.E2Method());
            Assert.Equal("E3", await IERef.E3Method());

        }

        /// <summary>
        /// 测试工厂返回多形态 grain 参考的法。
        /// 证明 grain 工厂能够使用一个接口类型创建 grains
        /// 返回它们为不同的兼容界面类型,
        /// 具有界面抽象化的有利工厂模式。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_FactoryMethods()
        {
            var grainFullName = typeof(PolymorphicTestGrain).FullName;
            IC ICRef = this.GrainFactory.GetGrain<IF>(GetRandomGrainId(), grainFullName); // Freef 工厂法返回 IPDrew 的多色引用
            Assert.Equal("B2", await ICRef.B2Method());

            IA IARef = this.GrainFactory.GetGrain<ID>(GetRandomGrainId(), grainFullName); // Dref 工厂方法返回一个多色引用 IARef
            Assert.Equal("A1", await IARef.A1Method());
        }

        /// <summary>
        /// 测试一个服务型 grain ,该型号可安装多个接口。
        /// 证明单 grain 可暴露多套功能
        /// 与暴露多个合同的服务类似。
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
        /// 测试如何解决钻石继承设想中的模糊方法。
        /// 当多个界面定义同一方法的签名时,当多个接口定义了相同的方法签名时,验证
        /// 选择特定界面正确解决执行哪个
        /// 被称作,遵循C#界面继承规则。
        /// </summary>
        [Fact, TestCategory("BVT"), TestCategory("Cast")]
        public async Task Polymorphic_InheritedMethodAmbiguity()
        {
            // 测试继承等级的交错,涉及方法名称重复,需要作出选择才能解决模糊问题。
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
        /// 复杂多形态 grain 情景的综合测试。
        /// 测试一个衍生服务类型,该类型可实施多个接口等级,
        /// 核实:
        /// - 继承链中的所有接口都无障碍
        /// - 支持时在不相关接口之间交叉播报
        /// - 与基本方法同时可用衍生接口法
        /// 这将合并所有多形态 grain 参考使用案例。
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


            IB IBRef = (IB)IARef; // 这可能会导致无效的投放例外,但不会发生,因为我们事先知道 DerivedServiceType 执行接口
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

            IServiceType serviceTypeRef = derivedRef; // 向上显示指针引用
            Assert.Equal("ServiceTypeMethod1", await serviceTypeRef.ServiceTypeMethod1());
            Assert.Equal("ServiceTypeMethod2", await serviceTypeRef.ServiceTypeMethod2());
            Assert.Equal("ServiceTypeMethod3", await serviceTypeRef.ServiceTypeMethod3());

            Assert.Equal("DerivedServiceTypeMethod1", await derivedRef.DerivedServiceTypeMethod1());
        }
    }
}
