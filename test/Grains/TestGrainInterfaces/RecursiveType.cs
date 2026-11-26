namespace UnitTests.GrainInterfaces
{
    public interface IReferenceRecursiveTypeGrain : IGrainWithGuidKey
    {
        Task<RecursiveType> Echo(RecursiveType arg);
    }

    /// <summary>
    /// 这些类组构成 https://github.com/dotnet/orleans/issues/5473的复正方程式,结果产生
    /// 代码生成时StackOverflowException
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public class RecursiveType : SelfTyped<RecursiveType>
    {
    }

    [GenerateSerializer]
    public abstract class SelfTyped<T> where T : SelfTyped<T>
    {
    }
}
