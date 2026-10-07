# Image of the Web API (SalesInventory.Api).
# Build from the repository root, because the API uses three other projects of the solution:
#   docker build -f src/SalesInventory.Api/Dockerfile -t salesinventory-api .

# ---- Stage 1: build (the SDK image: big, used only to compile) ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, from the project files alone: this layer is rebuilt only when a package changes, not on every edit of the code.
# Only the projects the API needs (Api -> Application, Infrastructure -> Domain), not the whole solution.
# -r linux-x64: restore for the one platform the image runs on (see the size note at the publish step)
COPY src/SalesInventory.Domain/SalesInventory.Domain.csproj src/SalesInventory.Domain/
COPY src/SalesInventory.Application/SalesInventory.Application.csproj src/SalesInventory.Application/
COPY src/SalesInventory.Infrastructure/SalesInventory.Infrastructure.csproj src/SalesInventory.Infrastructure/
COPY src/SalesInventory.Api/SalesInventory.Api.csproj src/SalesInventory.Api/
RUN dotnet restore src/SalesInventory.Api/SalesInventory.Api.csproj -r linux-x64

# Then the code of those four projects (no tests, no docs, no Blazor app)
COPY src/SalesInventory.Domain/ src/SalesInventory.Domain/
COPY src/SalesInventory.Application/ src/SalesInventory.Application/
COPY src/SalesInventory.Infrastructure/ src/SalesInventory.Infrastructure/
COPY src/SalesInventory.Api/ src/SalesInventory.Api/

# Size: publishing for ONE platform (-r linux-x64, framework-dependent) leaves out the native libraries of every other platform
# (Windows, ARM, musl) that NuGet packages such as QuestPDF and SqlClient carry: about 90 MB less in the image.
# UseAppHost=false: no native launcher is needed, the image starts the app with "dotnet SalesInventory.Api.dll"
RUN dotnet publish src/SalesInventory.Api/SalesInventory.Api.csproj -c Release -o /app/publish --no-restore \
    -r linux-x64 --self-contained false -p:UseAppHost=false

# The app writes two things at run time: the rolling log files (logs/, Serilog) and the product images (wwwroot/uploads/).
# The final image has no shell, so these folders are made here and handed over with COPY --chown below.
RUN mkdir -p /app/publish/logs /app/publish/wwwroot/uploads/products

# ---- Stage 2: final (the ASP.NET runtime image: no SDK, no source code) ---------------------------------------------------------
# "chiseled": Ubuntu with only what .NET needs: no shell, no package manager, already a non-root user. "-extra" adds ICU and tzdata,
# which this app needs (culture vi-VN for dates and money in PDFs). Debugging with "docker exec ... sh" is not possible here;
# use "docker compose logs api".
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled-extra AS final
WORKDIR /app

# The only thing that comes from the build stage: the published output. 1654 is the "app" user of the .NET images.
# Mount volumes on /app/logs and /app/wwwroot/uploads to keep that data.
COPY --from=build --chown=1654:1654 /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080
# A port above 1023, because a non-root user may not listen on a lower one. TLS is done by the reverse proxy in front of the container.
EXPOSE 8080

# Not root
USER 1654

ENTRYPOINT ["dotnet", "SalesInventory.Api.dll"]
