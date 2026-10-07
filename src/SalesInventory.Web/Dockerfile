# Image of the Blazor app (project SalesInventory.Web). It references no other project, so it builds on its own.
# Build from the repository root (the same context as the API image):
#   docker build -f src/SalesInventory.Web/Dockerfile -t salesinventory-web .

# ---- Stage 1: build (the SDK image: big, used only to compile) ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, from the project file alone: rebuilt only when a package changes
COPY src/SalesInventory.Web/SalesInventory.Web.csproj src/SalesInventory.Web/
RUN dotnet restore src/SalesInventory.Web/SalesInventory.Web.csproj

# Then the code of this one project
COPY src/SalesInventory.Web/ src/SalesInventory.Web/

RUN dotnet publish src/SalesInventory.Web/SalesInventory.Web.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

# ---- Stage 2: final (the ASP.NET runtime image: no SDK, no source code) ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# The only thing that comes from the build stage: the published output (including the static web assets of the components)
COPY --from=build /app/publish .

# The address of the API is NOT baked in: set it when the container starts, for example  -e ApiBaseUrl=http://api:8080
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080
# A port above 1023, because a non-root user may not listen on a lower one. TLS is done by the reverse proxy in front of the container.
EXPOSE 8080

# Not root: the "app" user (UID in $APP_UID) that the .NET images provide. The app writes nothing under /app.
USER $APP_UID

ENTRYPOINT ["dotnet", "SalesInventory.Web.dll"]
