using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 记录没有显式事务选项的 Grain 调用的事务归属。
    /// </summary>
    public class NoAttributionGrain : Grain, INoAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录抑制环境事务的 Grain 调用的事务归属。
    /// </summary>
    public class SuppressAttributionGrain : Grain, ISuppressAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录加入或创建事务的 Grain 调用的事务归属。
    /// </summary>
    public class CreateOrJoinAttributionGrain : Grain, ICreateOrJoinAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录创建新事务的 Grain 调用的事务归属。
    /// </summary>
    public class CreateAttributionGrain : Grain, ICreateAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录要求存在环境事务并加入该事务的 Grain 调用的事务归属。
    /// </summary>
    public class JoinAttributionGrain : Grain, IJoinAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录支持环境事务的 Grain 调用的事务归属。
    /// </summary>
    public class SupportedAttributionGrain : Grain, ISupportedAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录拒绝环境事务的 Grain 调用的事务归属。
    /// </summary>
    public class NotAllowedAttributionGrain : Grain, INotAllowedAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    internal static class AttributionGrain
    {
        public static async Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            TransactionInfo? ti = TransactionContext.GetTransactionInfo();
            List<string?>?[] results = new List<string?>[tier + 1 + tiers.Length];
            results[tier] = new List<string?>(new[] { ti?.Id });

            if (tiers.Length == 0)
            {
                return results;
            }

            List<ITransactionAttributionGrain> nextTier = tiers.FirstOrDefault()!;
            List<ITransactionAttributionGrain>[] nextTiers = tiers.Skip(1).ToArray();
            List<string?>?[][] tiersResults = await Task.WhenAll(nextTier.Select(g => g.GetNestedTransactionIds(tier + 1, nextTiers)));
            foreach (List<string?>?[] result in tiersResults)
            {
                if (result.Length != results.Length) throw new ApplicationException("Invalid result length");
                for (int i = tier + 1; i < results.Length; i++)
                {
                    if (results[i] != null)
                    {
                        results[i]!.AddRange(result[i]!);
                    }
                    else
                        results[i] = result[i];
                }
            }

            return results;
        }
    }
}
