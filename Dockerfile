# ---------- 1. frontend ----------
FROM node:22-bookworm-slim AS web
WORKDIR /web
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY frontend/ ./
RUN npm run build

# ---------- 2. backend ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/Tracker.Api/Tracker.Api.csproj backend/Tracker.Api/
RUN dotnet restore backend/Tracker.Api/Tracker.Api.csproj
COPY backend/Tracker.Api/ backend/Tracker.Api/
RUN dotnet publish backend/Tracker.Api/Tracker.Api.csproj -c Release -o /out --no-restore

# ---------- 3. runtime ----------
# The SDK image (not aspnet) because the app runs `dotnet test` to judge submissions.
FROM mcr.microsoft.com/dotnet/sdk:10.0
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    ASPNETCORE_URLS=http://+:8080 \
    Tracker__WorkDir=/var/tracker/build \
    NUGET_PACKAGES=/home/app/.nuget/packages

# Node + Claude Code CLI (optional AI provider)
COPY --from=web /usr/local/bin/node /usr/local/bin/node
COPY --from=web /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
 && npm install -g @anthropic-ai/claude-code --no-audit --no-fund \
 && npm cache clean --force

RUN mkdir -p /var/tracker/build /home/app/.nuget/packages /home/app/.claude && chown -R app:app /var/tracker /home/app

USER app
# Warm the NuGet cache with the DSA project's test packages so the first judged run is quick.
RUN mkdir -p /tmp/warm && cd /tmp/warm \
 && printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" /><PackageReference Include="xunit" Version="2.9.3" /><PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" /></ItemGroup></Project>' > warm.csproj \
 && dotnet restore warm.csproj && rm -rf /tmp/warm

WORKDIR /app
COPY --from=api --chown=app:app /out ./
COPY --from=web --chown=app:app /web/dist ./wwwroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tracker.Api.dll"]
