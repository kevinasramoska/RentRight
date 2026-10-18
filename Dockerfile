# Pinned multi-platform manifests; refresh tags and digests together (see deployment runbook).
FROM node:24-bookworm-slim@sha256:a5f4366e834695d5f363b920c104953a535d3a9864d1bdf74566a0ebe01daee5 AS client
WORKDIR /source/ClientApp
COPY ClientApp/package.json ClientApp/package-lock.json ./
RUN npm ci
COPY ClientApp/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /source
COPY global.json RentRight.csproj ./
RUN dotnet restore RentRight.csproj
COPY . .
COPY --from=client /source/wwwroot/app/ ./wwwroot/app/
RUN dotnet publish RentRight.csproj --configuration Release --no-restore --output /app/publish \
    /p:UseAppHost=false /p:BuildReactOnPublish=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS runtime
ARG VERSION=dev
ARG REVISION=unknown
LABEL org.opencontainers.image.title="RentRight" \
      org.opencontainers.image.description="ASP.NET Core and React property-management application" \
      org.opencontainers.image.source="https://github.com/kevinasramoska/RentRight" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "RentRight.dll"]
