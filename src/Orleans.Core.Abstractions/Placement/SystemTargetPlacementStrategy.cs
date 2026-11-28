namespace Orleans.Runtime
{
    /// <summary>
    /// 系统目标使用的放置策略。
    /// </summary>
    [GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class SystemTargetPlacementStrategy : PlacementStrategy
    {
        /// <summary>
        /// 获取此策略的单例实例。
        /// </summary>
        public static SystemTargetPlacementStrategy Instance { get; } = new();

        /// <inheritdoc/>
        public override bool IsUsingGrainDirectory => false;
    }
}
