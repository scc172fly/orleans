using UnitTests.FSharpInterfaces;

namespace UnitTests.GrainInterfaces
{
    // 取消以下接口定义的注释即可复现 #1349

    public interface IGeneratorTestDerivedFromFSharpInterfaceInExternalAssemblyGrain : IGrainWithGuidKey, IFSharpBaseInterface
    {
    }
}
