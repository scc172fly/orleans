// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Abstractions;

public interface IShoppingCartGrain : IGrainWithStringKey
{
    /// <summary>
    /// 添加对应的给定 <paramref name="quantity"/>
    /// <paramref name="product"/> 上购物车。
    /// </summary>
    Task<bool> AddOrUpdateItemAsync(int quantity, ProductDetails product);

    /// <summary>
    /// 从购物车中删除给定的 <paramref name="product" /> 。
    /// </summary>
    Task RemoveItemAsync(ProductDetails product);

    /// <summary>
    /// 买下购物车里的所有物品
    /// </summary>
    Task<HashSet<CartItem>> GetAllItemsAsync();

    /// <summary>
    /// 获得购物车内的项目数量。
    /// </summary>
    Task<int> GetTotalItemsInCartAsync();

    /// <summary>
    /// 从购物车中删除所有项目。
    /// </summary>
    Task EmptyCartAsync();
}
