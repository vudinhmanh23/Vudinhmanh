FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY SalesInventory.sln ./
COPY src/SalesInventory.Domain/SalesInventory.Domain.csproj src/SalesInventory.Domain/
COPY src/SalesInventory.Application/SalesInventory.Application.csproj src/SalesInventory.Application/
COPY src/SalesInventory.Infrastructure/SalesInventory.Infrastructure.csproj src/SalesInventory.Infrastructure/
COPY src/SalesInventory.Api/SalesInventory.Api.csproj src/SalesInventory.Api/
RUN dotnet restore SalesInventory.sln

COPY . .
RUN dotnet publish src/SalesInventory.Api/SalesInventory.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SalesInventory.Api.dll"]
