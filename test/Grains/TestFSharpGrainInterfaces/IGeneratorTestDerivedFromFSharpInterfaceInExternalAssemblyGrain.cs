using UnitTests.FSharpInterfaces;

namespace UnitTests.GrainInterfaces
{
    // 取消以下复制 #1349 的界面定义

    public interface IGeneratorTestDerivedFromFSharpInterfaceInExternalAssemblyGrain : IGrainWithGuidKey, IFSharpBaseInterface
    {
    }
}
