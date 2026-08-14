# Build a partir da RAIZ do repositorio:
#   docker build -f src/Mamao.Api/Dockerfile -t mamao-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore em camada propria: so refaz quando csproj/props mudam.
COPY Directory.Build.props Directory.Packages.props NuGet.config ./
COPY src/ src/
RUN dotnet restore src/Mamao.Api/Mamao.Api.csproj

RUN dotnet publish src/Mamao.Api/Mamao.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# A imagem da Microsoft nao traz curl nem wget, e o healthcheck do compose roda DENTRO
# do container. Sem isso o healthcheck falha sempre — e um container eternamente
# "unhealthy" e pior que nenhum healthcheck: parece vigilancia e nao e.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Nunca rodar como root.
#
# Os diretorios de volume precisam existir E pertencer ao usuario AQUI, na imagem:
# quando o Docker monta um volume nomeado vazio, ele copia dono e permissao do que
# encontra na imagem. Sem isso o volume nasce do root, o processo (64198) nao escreve,
# e o Data Protection quebra ao gravar a chave — sem chave nao ha recuperacao de senha
# nem convite.
RUN useradd --uid 64198 --create-home mamao \
    && mkdir -p /var/mamao/keys /var/mamao/uploads \
    && chown -R 64198:64198 /var/mamao
USER 64198

COPY --from=build --chown=64198 /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Mamao.Api.dll"]
