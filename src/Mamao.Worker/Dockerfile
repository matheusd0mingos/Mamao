# Build a partir da RAIZ do repositorio:
#   docker build -f src/Mamao.Worker/Dockerfile -t mamao-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada propria: so refaz quando csproj/props mudam.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/ src/
RUN dotnet restore src/Mamao.Worker/Mamao.Worker.csproj

RUN dotnet publish src/Mamao.Worker/Mamao.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Ver o Dockerfile da API: a imagem base nao traz curl, e o healthcheck precisa dele.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Nunca rodar como root.
RUN useradd --uid 64198 --create-home mamao
USER 64198

COPY --from=build --chown=64198 /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Mamao.Worker.dll"]
