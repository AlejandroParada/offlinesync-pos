FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json nuget.config ./
COPY src/OfflineSync.Domain/ src/OfflineSync.Domain/
COPY src/OfflineSync.Application/ src/OfflineSync.Application/
COPY src/OfflineSync.Infrastructure/ src/OfflineSync.Infrastructure/
COPY src/OfflineSync.Api/ src/OfflineSync.Api/
RUN dotnet publish src/OfflineSync.Api/OfflineSync.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OfflineSync.Api.dll"]
