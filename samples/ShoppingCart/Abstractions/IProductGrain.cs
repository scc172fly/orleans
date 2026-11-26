// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Abstractions;

public interface IProductGrain : IGrainWithStringKey
{
    Task<(bool IsAvailable, ProductDetails? ProductDetails)> TryTakeProductAsync(int quantity);

    Task ReturnProductAsync(int quantity);

    Task<int> GetProductAvailabilityAsync();

    Task CreateOrUpdateProductAsync(ProductDetails productDetails);

    Task<ProductDetails> GetProductDetailsAsync();
}
