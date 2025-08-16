ARG NODE_VERSION=22.17
ARG DOTNET_VERSION=10.0-preview-alpine

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS base
USER app
WORKDIR /app

FROM node:${NODE_VERSION}-alpine AS node-base

ARG NPM_REGISTRY=https://registry.npmjs.org/

ENV COREPACK_NPM_REGISTRY=${NPM_REGISTRY}
ENV PNPM_HOME="/pnpm"
ENV PATH="$PNPM_HOME:$PATH"

RUN corepack enable
# RUN npm install -g pnpm

FROM node-base as node-install

COPY ["./certs-server/package.json", "./certs-server/pnpm-lock.yaml", "./certs-server/tsconfig.json", "./certs-server/tailwind.config.css", "./certs-server/tsconfig.node.json", "./certs-server/vite.config.ts", "/src/"]

WORKDIR /src
RUN --mount=type=cache,id=pnpm,target=/pnpm/store pnpm install --frozen-lockfile

FROM node-install AS node-build
COPY ["./certs-server", "."]

RUN pnpm run build

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS dotnet-restore
ARG BUILD_CONFIGURATION=${BUILD_CONFIGURATION:-Release}

COPY ["./acme/acme.csproj", "/src/acme/"]
COPY ["./certs-server/certs-server.csproj", "/src/certs-server/"]
# COPY ["./cli/cli.csproj", "/src/cli/"]
# COPY ["./sqlite-migrations/sqlite-migrations.csproj", "/src/sqlite-migrations/"]
COPY ["./Directory.Packages.props", "/src/"]
COPY ["./Directory.Build.props", "/src/"]
COPY ["./nuget.config", "/src/"]

WORKDIR /src
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet restore "./certs-server/certs-server.csproj"

FROM dotnet-restore AS dotnet-build

COPY ["./acme", "./acme/"]
COPY ["./certs-server", "./certs-server/"]
# COPY ["./cli", "./cli/"]
# COPY ["./eventbus", "./eventbus/"]
# COPY ["./sqlite-migrations", "./sqlite-migrations/"]
COPY --from=node-build ["/src/wwwroot/dist/.vite", "/src/wwwroot/dist/assets", "/src/wwwroot/dist/", "./certs-server/wwwroot/"]

RUN rm -rf "/src/certs-server/wwwroot/dist"

RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet build "./certs-server/certs-server.csproj" /p:EnablePublishBuildAssets=false

FROM dotnet-build AS publish
ARG BUILD_CONFIGURATION=Release
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages dotnet publish "./certs-server/certs-server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:EnablePublishBuildAssets=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "certs-server.dll"]