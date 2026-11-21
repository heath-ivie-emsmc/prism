# syntax=docker/dockerfile:1
# One image serves the API and the SPA. Build from the repo root:
#   docker build -f docker/Dockerfile --secret id=npm_token,env=NPM_TOKEN -t prism .
# NPM_TOKEN needs read access to the @ems-management-consultants packages on GitHub Packages. It is mounted for the install only
# and is not stored in any layer.

FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json web/.npmrc ./
RUN --mount=type=secret,id=npm_token \
    NPM_TOKEN="$(cat /run/secrets/npm_token)" npm ci --ignore-scripts
COPY web/ ./
RUN npx ng build

FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Prism.slnx ./
COPY src/ src/
RUN dotnet restore src/Prism.Api/Prism.Api.csproj
COPY --from=web /web/dist/web/browser src/Prism.Api/wwwroot
RUN dotnet publish src/Prism.Api/Prism.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# The base image's non-root user. Kestrel listens on 8080, so no privileged port is needed.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Prism.Api.dll"]
