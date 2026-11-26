#查看 https://aka.ms/customizecontainer 学习如何定制调试容器,以及视觉工作室如何使用此 Docker 文件构建图像以更快地调试 。

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY . .
RUN dotnet restore "samples/ShoppingCart/Silo/Orleans.ShoppingCart.Silo.csproj" --configfile samples/NuGet.config
WORKDIR "/src/samples/ShoppingCart/Silo"
RUN dotnet build "Orleans.ShoppingCart.Silo.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Orleans.ShoppingCart.Silo.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Orleans.ShoppingCart.Silo.dll"]
