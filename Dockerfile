FROM mcr.microsoft.com/dotnet/sdk:10.0 as build
WORKDIR /build
RUN git init dev && cd dev && git remote add origin https://github.com/Coflnet/HypixelSkyblock.git \
    && git fetch --depth=1 origin df02c6a675c68a54be6a7ad58a5fcd379db97e42 \
    && git checkout --detach FETCH_HEAD
WORKDIR /build/sky
COPY SkyMcConnect.csproj SkyMcConnect.csproj
RUN dotnet restore
COPY . .
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8000

# apt in the base image has CVE-2026-109518 (HIGH); upgrade it until the base image ships the fix.
RUN apt-get update \
    && apt-get install -y --only-upgrade --no-install-recommends apt libapt-pkg6.0t64 \
    && rm -rf /var/lib/apt/lists/*
RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user
USER app-user

ENTRYPOINT ["dotnet", "SkyMcConnect.dll", "--hostBuilder:reloadConfigOnChange=false"]