// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Abstractions;

public interface IInventoryGrain : IGrainWithStringKey
{
    Task<HashSet<ProductDetails>> GetAllProductsAsync();

    Task AddOrUpdateProductAsync(ProductDetails productDetails);

    Task RemoveProductAsync(string productId);
}
