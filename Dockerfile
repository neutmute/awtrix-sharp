# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.
ARG GIT_COMMIT
ARG GIT_COMMIT_SHORT

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app-api
EXPOSE 8080
EXPOSE 8081

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY src/api/awtrix-api.csproj api/
RUN dotnet restore api/awtrix-api.csproj
COPY src/ .
WORKDIR /src/api
RUN dotnet build awtrix-api.csproj -c $BUILD_CONFIGURATION -o /app-api/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish awtrix-api.csproj -c $BUILD_CONFIGURATION -o /app-api/publish /p:UseAppHost=false /p:GIT_COMMIT=$GIT_COMMIT /p:GIT_COMMIT_SHORT:$GIT_COMMIT_SHORT

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app-api
COPY --from=publish /app-api/publish .
ENTRYPOINT ["dotnet", "awtrix-api.dll"]