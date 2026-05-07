# ============================================
# Stage 1: Build
# ============================================
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview AS build
WORKDIR /src

# Copy NuGet config (includes preview feed)
COPY nuget.config ./

# Restore dependencies (leveraging layer caching)
COPY ExcelImportSystem.Core/*.csproj ExcelImportSystem.Core/
COPY ExcelImportSystem.Infrastructure/*.csproj ExcelImportSystem.Infrastructure/
COPY ExcelImportSystem.API/*.csproj ExcelImportSystem.API/
RUN dotnet restore ExcelImportSystem.API/ExcelImportSystem.API.csproj

# Copy full source and publish
COPY . .
RUN dotnet publish ExcelImportSystem.API/ExcelImportSystem.API.csproj \
    -c Release \
    -o /app \
    --no-restore

# ============================================
# Stage 2: Runtime
# ============================================
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0-preview AS runtime
WORKDIR /app

# Create logs directory
RUN mkdir -p /app/logs

# Copy published output
COPY --from=build /app .

# Expose API port
EXPOSE 5000

# Run the application
ENTRYPOINT ["dotnet", "ExcelImportSystem.API.dll"]
