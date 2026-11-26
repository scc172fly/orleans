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
            // orleans 未经配置 DI 时将使用此构建器
        }

        public MultipleConstructorsSimpleGrain(ILoggerFactory loggerFactory, int initialValueofA) : base(loggerFactory)
        {
            base.A = initialValueofA;
        }
    }
}
