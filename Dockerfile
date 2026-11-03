# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/IHub.Api/IHub.Api.csproj src/IHub.Api/
RUN dotnet restore src/IHub.Api/IHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/IHub.Api/IHub.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080

USER $APP_UID

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "IHub.Api.dll"]
