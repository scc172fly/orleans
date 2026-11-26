using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans;
using Orleans.Dashboard.Implementation;
using Orleans.Runtime;
using Xunit;

namespace UnitTests;

[TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("None")]
[TestArea("Dashboard")]
public class LifecycleStageInspectorTests
{
    [Fact]
    public void GetStages_GroupsObserversByStage_AndPreservesOrder()
    {
        var lifecycle = new SiloLifecycleSubject(NullLogger<SiloLifecycleSubject>.Instance);

        lifecycle.Subscribe("A", ServiceLifecycleStage.RuntimeInitialize, Start, Stop);
        lifecycle.Subscribe("B", ServiceLifecycleStage.RuntimeServices, Start, Stop);
        lifecycle.Subscribe("C", ServiceLifecycleStage.RuntimeServices, Start, Stop);
        lifecycle.Subscribe("D", 4242, Start, Stop); // 仅数级

        var stages = LifecycleStageInspector.GetStages(lifecycle);

        // 三个不同的阶段,按顺序升
        Assert.Equal(3, stages.Length);
        Assert.Equal(ServiceLifecycleStage.RuntimeInitialize, stages[0].Stage);
        Assert.Equal(ServiceLifecycleStage.RuntimeServices, stages[1].Stage);
        Assert.Equal(4242, stages[2].Stage);

        // 标注为此的命名阶段; 数字化的阶段不是。
        Assert.True(stages[0].IsNamedStage);
        Assert.True(stages[1].IsNamedStage);
        Assert.False(stages[2].IsNamedStage);

        // 观察员按登记姓名的每个阶段分组。
        Assert.Single(stages[0].Observers);
        Assert.Equal("A", stages[0].Observers[0].Name);
        Assert.Equal(2, stages[1].Observers.Length);
        Assert.Equal(["B", "C"], stages[1].Observers.Select(o => o.Name).ToArray());
        Assert.Single(stages[2].Observers);
        Assert.Equal("D", stages[2].Observers[0].Name);

        // 代表制用户披露 OnStart/OnStop的方法名称。
        var aObserver = stages[0].Observers[0];
        Assert.True(aObserver.HasOnStart);
        Assert.True(aObserver.HasOnStop);
        Assert.Contains("Start", aObserver.OnStartMethod, System.StringComparison.Ordinal);
        Assert.Contains("Stop", aObserver.OnStopMethod, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GetStages_StartupOnlyObserver_IsRecognisedAsStartOnly()
    {
        var lifecycle = new SiloLifecycleSubject(NullLogger<SiloLifecycleSubject>.Instance);

        // 订阅超载而不设置 onStop 创建 StartupObserver。
        lifecycle.Subscribe("StartOnly", ServiceLifecycleStage.ApplicationServices, Start);

        var stages = LifecycleStageInspector.GetStages(lifecycle);
        var observer = Assert.Single(Assert.Single(stages).Observers);

        Assert.True(observer.HasOnStart);
        Assert.False(observer.HasOnStop);
        Assert.NotNull(observer.OnStartMethod);
        Assert.Null(observer.OnStopMethod);
    }

    [Fact]
    public void GetStages_HandlesNullLifecycle()
    {
        var stages = LifecycleStageInspector.GetStages(null);
        Assert.Empty(stages);
    }

    [Fact]
    public void GetStages_PrettyPrintsStaticLocalFunctionNames_EvenInExplicitInterfaceImplementations()
    {
        // 通过 Orleans.ILifecycleParticipant<ISiloLifecycle>订阅的类型。
        // ( 直径界面) 。 此练习嵌套的外名路径
        // LifecycleStageInspector.CleanMethodName中。
        var subject = new SiloLifecycleSubject(NullLogger<SiloLifecycleSubject>.Instance);
        var participant = new ExplicitInterfaceParticipant();
        ((ILifecycleParticipant<ISiloLifecycle>)participant).Participate(subject);

        var stages = LifecycleStageInspector.GetStages(subject);
        var observer = Assert.Single(Assert.Single(stages).Observers);

        Assert.Equal("MyStaticLocalStart", observer.OnStartMethod?.Split('.')[^1]);
        Assert.Equal("MyStaticLocalStop", observer.OnStopMethod?.Split('.')[^1]);
        Assert.DoesNotContain("g__", observer.OnStartMethod ?? string.Empty, System.StringComparison.Ordinal);
        Assert.DoesNotContain("g__", observer.OnStopMethod ?? string.Empty, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GetStages_LabelsAnonymousLambdas_AsLambdaInOuterMethod()
    {
        var subject = new SiloLifecycleSubject(NullLogger<SiloLifecycleSubject>.Instance);
        subject.Subscribe("WithLambda", ServiceLifecycleStage.ApplicationServices, _ => Task.CompletedTask, _ => Task.CompletedTask);

        var stages = LifecycleStageInspector.GetStages(subject);
        var observer = Assert.Single(Assert.Single(stages).Observers);

        // 应在该测试方法内报告匿名羊排,作为
        // `<lambda in GetStages_LabelsAnonymousLambdas_AsLambdaInOuterMethod>` 而不是
        // 曝光编译器生成的 b__N_N 后缀。
        Assert.Contains("<lambda in", observer.OnStartMethod ?? string.Empty, System.StringComparison.Ordinal);
        Assert.Contains("<lambda in", observer.OnStopMethod ?? string.Empty, System.StringComparison.Ordinal);
        Assert.DoesNotContain("b__", observer.OnStartMethod ?? string.Empty, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GetStages_FormatsNumericStageUsingCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = culture;
            var subject = new SiloLifecycleSubject(NullLogger<SiloLifecycleSubject>.Instance);
            subject.Subscribe("CustomStage", -4242, Start, Stop);

            var stage = Assert.Single(LifecycleStageInspector.GetStages(subject));

            Assert.Equal("~4242", stage.StageName);
            Assert.False(stage.IsNamedStage);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static Task Start(CancellationToken ct) => Task.CompletedTask;

    private static Task Stop(CancellationToken ct) => Task.CompletedTask;

    private sealed class ExplicitInterfaceParticipant : ILifecycleParticipant<ISiloLifecycle>
    {
        void ILifecycleParticipant<ISiloLifecycle>.Participate(ISiloLifecycle observer)
        {
            observer.Subscribe(nameof(ExplicitInterfaceParticipant), ServiceLifecycleStage.RuntimeServices, MyStaticLocalStart, MyStaticLocalStop);

            static Task MyStaticLocalStart(CancellationToken _) => Task.CompletedTask;
            static Task MyStaticLocalStop(CancellationToken _) => Task.CompletedTask;
        }
    }
}
