// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Silo.Authorization;

internal static class AuthorizationPolicies
{
    public const string ProductManagement = nameof(ProductManagement);
    public const string ProductAdministratorRole = "ProductAdministrator";
}
