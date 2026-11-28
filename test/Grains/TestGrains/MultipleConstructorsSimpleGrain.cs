using Microsoft.Extensions.Logging;
using UnitTests.GrainInterfaces;

namespace UnitTests.Grains
{
    public class MultipleConstructorsSimpleGrain : SimpleGrain, ISimpleGrain
    {
        public const string MultipleConstructorsSimpleGrainPrefix = "UnitTests.Grains.MultipleConstructorsS";
        public const int ValueUsedByParameterlessConstructor = 42;

        public MultipleConstructorsSimpleGrain(ILoggerFactory loggerFactory)
            : this(loggerFactory, ValueUsedByParameterlessConstructor)
        {
            // 未配置依赖注入（DI）时，Orleans 将使用此构造函数
        }

        public MultipleConstructorsSimpleGrain(ILoggerFactory loggerFactory, int initialValueofA) : base(loggerFactory)
        {
            base.A = initialValueofA;
        }
    }
}
