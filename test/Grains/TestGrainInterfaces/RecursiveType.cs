namespace UnitTests.GrainInterfaces
{
    public interface IReferenceRecursiveTypeGrain : IGrainWithGuidKey
    {
        Task<RecursiveType> Echo(RecursiveType arg);
    }

    /// <summary>
    /// 这些类用于复现 https://github.com/dotnet/orleans/issues/5473 的问题，
    /// 该问题会在代码生成期间引发 StackOverflowException。
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
