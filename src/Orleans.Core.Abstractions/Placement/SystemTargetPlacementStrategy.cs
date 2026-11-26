namespace Orleans.Runtime
{
    /// <summary>
    /// 系统目标所使用的安置战略。
    /// </summary>
    [GenerateSerializer, Immutable, SuppressReferenceTracking]
    public sealed class SystemTargetPlacementStrategy : PlacementStrategy
    {
        /// <summary>
        /// 获得这一策略的单核体实例。
        /// </summary>
        public static SystemTargetPlacementStrategy Instance { get; } = new();

        /// <inheritdoc/>
        public override bool IsUsingGrainDirectory => false;
    }
}
