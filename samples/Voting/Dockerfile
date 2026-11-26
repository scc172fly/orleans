#查看 https://aka.ms/containerfastmode 要了解视觉工作室如何使用此 Docker 文件构建您的图像以更快地调试 。

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
EXPOSE 11111
EXPOSE 8888
ENV ASPNETCORE_HTTP_PORTS=80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore "samples/Voting/Voting.csproj" --configfile samples/NuGet.config
WORKDIR "/src/samples/Voting"
RUN dotnet build "Voting.csproj" -c Release -o /app/build --no-restore

FROM build AS publish
RUN dotnet publish "Voting.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Voting.dll"]