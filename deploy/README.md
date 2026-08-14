# Deploy

Cloudflare → Caddy → Docker Compose, num VPS de nó único.
Decisões em [ADR-0011](../docs/adr/0011-aspire-e-deploy.md) e
[infraestrutura](../docs/arquitetura/infraestrutura-e-deploy.md).

**Dois caminhos.** Escolha um e ignore o outro:

| | `no-servidor.sh` | `deploy.sh` |
|---|---|---|
| Roda | no VPS, dentro do repositório clonado | na sua máquina (ou no CI), por SSH |
| Precisa de | Docker no servidor | registry, dois tokens, SSH configurado |
| Constrói | no próprio servidor, dentro de containers | na sua máquina, publica no registry |
| Bom quando | um servidor, sem CI, começando | há CI, ou o servidor não pode gastar CPU com build |

Comece pelo primeiro. O segundo continua aqui para quando o build no servidor incomodar.

| Arquivo | Papel |
|---|---|
| `no-servidor.sh` | **Caminho simples:** um comando, no servidor |
| `deploy.sh` | Caminho com registry: da sua máquina, por SSH |
| `docker-compose.yml` | Topologia de produção |
| `Caddyfile` | TLS, estáticos do Angular, proxy de `/api` |
| `init-db.sql` | Cria o role `mamao_app` (sem `BYPASSRLS`) na primeira subida |
| `backup.sh` | Dump + uploads, criptografado, enviado para fora do VPS |
| `.env.example` | Referência da config do **deploy** — só o caminho com registry usa |
| `.env.producao.example` | Referência dos **segredos**; ambos os caminhos geram os seus no servidor |

---

## Caminho simples

Uma vez, no servidor, para o `git clone` funcionar sem token:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/github -N '' -q && cat ~/.ssh/github.pub
```

Cole a saída em **Settings → Deploy keys → Add deploy key** do repositório, deixando
*Allow write access* **desmarcado**. Chave de deploy é somente leitura e vale só para
este repositório — diferente de um token pessoal, que dá acesso a todos os seus.

```bash
printf 'Host github.com\n  IdentityFile ~/.ssh/github\n' >> ~/.ssh/config
git clone git@github.com:matheusd0mingos/Mamao.git /opt/mamao-src
```

E o deploy, agora e sempre:

```bash
cd /opt/mamao-src
git pull
sudo ./deploy/no-servidor.sh
```

Na primeira vez ele instala o Docker se faltar, pergunta o domínio, gera as senhas e
sobe. Nas seguintes, só constrói e sobe. Não sobrescreve segredo nenhum.

Rollback é `git checkout <sha> && sudo ./deploy/no-servidor.sh`.

**Antes do primeiro deploy:** aponte o domínio para o IP do VPS no Cloudflare com o
proxy (nuvem laranja) **desligado**, senão o Caddy não consegue emitir o certificado.
Ligue depois que o site abrir em HTTPS.

**Depois do primeiro deploy:** preencha o SMTP em `/opt/mamao/.env` e reinicie
(`cd /opt/mamao && docker compose restart api`). Sem SMTP não há convite nem
recuperação de senha.

---

## Caminho com registry

Tudo daqui para baixo é o **outro** caminho. Se você está usando o `no-servidor.sh`,
pode pular direto para [Backup](#backup) — que vale para os dois.

### Primeira configuração

O `deploy.sh` faz o provisionamento. Rodar `./deploy/deploy.sh` numa máquina virgem
pergunta o que falta, cria o que não existe e segue para o deploy. Se preferir separar
as duas coisas, `--setup` só prepara o ambiente.

> **O repositório não vai para o servidor.** O script roda da sua máquina e conduz o
> servidor por SSH; o que chega lá são as imagens (pelo registry), o `dist` do Angular
> e os arquivos desta pasta. Não é preciso `git clone` nem `gh` no VPS — o servidor
> nunca precisa de acesso ao GitHub.

**A única coisa que ele não faz é criar o usuário SSH** — precisa de root, e é a única
etapa que você faz uma vez na vida do servidor:

```bash
ssh root@vps
adduser --disabled-password --gecos '' mamao
mkdir -p /home/mamao/.ssh && cp ~/.ssh/authorized_keys /home/mamao/.ssh/
chown -R mamao:mamao /home/mamao/.ssh
chmod 700 /home/mamao/.ssh && chmod 600 /home/mamao/.ssh/authorized_keys

# sudo SEM senha: o deploy roda sem terminal do outro lado, então um sudo que
# pergunta a senha não tem onde perguntar — morre no meio da instalação.
echo 'mamao ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/mamao
chmod 440 /etc/sudoers.d/mamao
```

O `NOPASSWD` incomoda de início, mas o usuário só entra por chave e a alternativa real
seria guardar a senha do sudo em algum lugar para o script usar — pior. Quando o
servidor estiver estável, dá para reduzir o escopo para os comandos que o script
realmente usa (`apt-get`, `mkdir`, `chown`, `usermod`).

Depois disso, feche o SSH por senha (`PasswordAuthentication no`) e o login de root
antes de expor a máquina.

A partir daí:

```bash
docker login ghcr.io          # token com write:packages
./deploy/deploy.sh --setup    # ou direto ./deploy/deploy.sh
```

### Duas credenciais de registry, de propósito

As imagens no ghcr nascem **privadas**, então o servidor precisa de credencial para
baixar. São duas, e diferentes:

| Quem | Para quê | Escopo | Onde fica |
|---|---|---|---|
| Sua máquina (ou o CI) | Publicar | `write:packages` | `docker login` local; no CI, o `GITHUB_TOKEN` do job |
| O servidor | Baixar | `read:packages` **e nada mais** | `~/.docker/config.json` no servidor |

O script pergunta o token de leitura na primeira vez e faz o `docker login` no servidor
por você. Um token de publicação guardado no servidor deixaria quem entrasse nele
**substituir a imagem da sua própria aplicação** — por isso o de lá só lê.

Essa credencial fica em base64 no `~/.docker/config.json`, que é como o Docker guarda
(não é criptografia). É o motivo de ela ser a mais fraca possível.

### O que o setup faz

| Verifica | Se faltar |
|---|---|
| `deploy/.env` | Pergunta host, usuário, porta, diretório, registry e origem pública, e cria com modo 600 |
| Conexão SSH | Falha explicando como criar o usuário |
| Docker + plugin compose | Oferece instalar pelo repositório oficial (pede confirmação, usa sudo) |
| Login do servidor no registry | Pede um token `read:packages` e autentica lá (sem ele o `pull` falha) |
| `$REMOTE_DIR` e `web-dist/` | Cria; recorre a sudo se `/opt` exigir |
| `$REMOTE_DIR/.env` | **Gera as senhas no próprio servidor** (`openssl`) e grava com modo 600 |
| compose, Caddyfile, init-db, backup.sh | Envia (e reenvia a cada deploy, para não dessincronizar) |

Tudo idempotente. Rodar de novo não sobrescreve nada — em especial **nunca** o `.env`
de produção: sobrescrever ali trocaria a senha do banco e derrubaria o acesso aos dados.

As senhas são geradas **no servidor**, não na sua máquina, e nunca transitam pela rede.
Guarde uma cópia do `BACKUP_PASSPHRASE` em outro lugar: sem ela o backup criptografado
não pode ser restaurado.

Em ambiente sem terminal (CI), use `--sim` para responder afirmativamente às
confirmações. Sem terminal e sem `--sim`, o script para em vez de adivinhar.

### DNS

Aponte `app.mamao.tech` para o IP do VPS no Cloudflare. Deixe o proxy (nuvem laranja)
**desligado no primeiro deploy**, para o Caddy conseguir emitir o certificado; ligue
depois. O script avisa se a borda não responder.

---

## Deploy

```bash
./deploy/deploy.sh              # testes → imagens → push → envio → subida → readiness
./deploy/deploy.sh --setup      # só prepara o ambiente
./deploy/deploy.sh --status     # o que está no ar
./deploy/deploy.sh --rollback   # volta para a versão anterior
./deploy/deploy.sh --skip-tests # só quando você já rodou a suíte
./deploy/deploy.sh --sim        # não pergunta nada (CI)
```

### Pelo CI (recomendado)

`.github/workflows/deploy.yml`, em **Actions → Deploy → Run workflow**. Ele chama o
mesmo `deploy.sh` — a lógica não está duplicada em YAML, porque duas implementações do
mesmo deploy divergem em silêncio e a diferença aparece no pior momento.

Vantagem sobre rodar da sua máquina: a imagem sai sempre do mesmo lugar, nenhuma
máquina de trabalho precisa do toolchain completo, e não existe a pergunta "de onde
saiu a versão que está no ar".

Configure uma vez, em **Settings → Secrets and variables → Actions**:

| Secret | O que é |
|---|---|
| `DEPLOY_SSH_KEY` | Chave privada do usuário `mamao` (gere um par só para o CI) |
| `DEPLOY_SSH_HOST` | IP ou domínio do VPS |
| `GHCR_READ_TOKEN` | PAT com **apenas** `read:packages` — é o que o servidor usa para baixar |

| Variable (opcional) | Padrão |
|---|---|
| `DEPLOY_SSH_USER` | `mamao` |
| `DEPLOY_SSH_PORT` | `22` |
| `DEPLOY_REMOTE_DIR` | `/opt/mamao` |
| `PUBLIC_ORIGIN` | `https://app.mamao.tech` |

O par de chaves do CI, na sua máquina:

```bash
ssh-keygen -t ed25519 -f ~/.ssh/mamao_ci -C 'deploy-ci' -N ''
cat ~/.ssh/mamao_ci.pub    # adicione em /home/mamao/.ssh/authorized_keys no servidor
cat ~/.ssh/mamao_ci        # cole inteiro no secret DEPLOY_SSH_KEY
```

O workflow usa o environment `producao`: configure-o no GitHub se quiser exigir
aprovação manual antes de cada deploy.

### O que o script faz, na ordem:

1. Confere ferramentas locais; prepara o ambiente se ainda não estiver pronto
2. Avisa se há alteração não commitada (a imagem é marcada com o SHA do commit)
3. Roda a suíte
4. Constrói e publica `mamao-api` e `mamao-worker` marcadas com o SHA — **nunca `latest`**
5. Constrói o Angular e envia o `dist` por rsync
6. Envia compose, Caddyfile, init-db e backup
7. `docker compose pull && up -d`
8. **Espera o `/healthz/ready`**, que só fica verde quando as migrations foram aplicadas
9. Confere pela borda pública
10. Registra a versão e guarda a anterior para rollback
11. Limpa imagens com mais de 7 dias

Se qualquer passo depois da subida falhar, o script volta sozinho para a versão anterior.

### O que o rollback não faz

Volta o **código**, não o **banco**. Migration já aplicada continua aplicada. Se a versão
com problema fez mudança destrutiva de schema, o caminho é restaurar o backup — por isso
mudança destrutiva deve ser feita em duas etapas (adicionar o novo, migrar, remover o
antigo num deploy posterior), nunca de uma vez.

### Indisponibilidade

Nó único: o `up -d` recria os containers e há alguns segundos de indisponibilidade.
Aceitável neste estágio — faça fora do horário comercial do cliente. Rolling update só
quando houver cliente que reclame.

---

## Backup

```bash
ssh mamao@vps 'crontab -e'
# 15 3 * * * /opt/mamao/backup.sh >> /var/log/mamao-backup.log 2>&1
```

Requer `rclone` configurado no servidor com um destino **fora do VPS** (B2, S3, R2) e
`gpg` instalado. Retenção: 7 diários, 4 semanais, 6 mensais.

## Restore — **ensaiado, e você precisa repetir uma vez**

Backup nunca restaurado não é backup, é esperança. O procedimento abaixo foi executado
de ponta a ponta contra um banco real do Mamão — dump, criptografia, descriptografia,
restauração e conferência — e voltou **6 funcionários, 20 ausências, 5 missões, 8
escalações e 33 registros de auditoria, com zero erro**.

Duas coisas que o ensaio confirmou e que ninguém pensa em checar:

- **A RLS volta junto.** As 9 policies e o `FORCE ROW LEVEL SECURITY` das nove tabelas
  vêm no dump. Um restore que perdesse isso devolveria o sistema no ar sem isolamento
  entre clientes — funcionando, e vazando.
- **O role `mamao_app` NÃO vem.** Ele é objeto do cluster, não do banco. Num servidor
  novo quem o recria é o Worker no startup, com a senha do `.env`. Se você restaurar num
  Postgres onde ele não existe, suba o Worker antes de apontar a API.

Faça o exercício inteiro uma vez, cronometrado, com um backup de verdade do seu servidor.
O tempo aqui foi de 2 segundos com um banco pequeno; o que importa é você medir o **seu**,
porque é esse número que você vai prometer para o cliente.

```bash
# 1. Baixe e descriptografe
rclone copy b2:mamao-backups/2026/08/mamao-20260814T031500Z.dump.gpg .
gpg --batch --passphrase "$BACKUP_PASSPHRASE" -d mamao-20260814T031500Z.dump.gpg \
    > mamao.dump

# 2. Suba um Postgres limpo
docker run -d --name restore-teste -e POSTGRES_PASSWORD=teste -p 55432:5432 postgres:17-alpine
sleep 5
docker exec restore-teste createdb -U postgres mamao

# 3. Restaure
docker cp mamao.dump restore-teste:/tmp/
docker exec restore-teste pg_restore -U postgres -d mamao --no-owner /tmp/mamao.dump

# 4. Confira os dados E o isolamento
docker exec restore-teste psql -U postgres -d mamao \
    -c 'SELECT count(*) FROM people.employees;' \
    -c 'SELECT count(*) FROM identity.tenants;' \
    -c "SELECT count(*) FROM pg_policies WHERE schemaname IN ('people','audit');"

# 5. Os uploads, e um arquivo abrindo de verdade
gpg --batch --passphrase "$BACKUP_PASSPHRASE" -d uploads-20260814T031500Z.tar.gz.gpg \
    | tar -tzf - | head

docker rm -f restore-teste
```

Anote quanto levou. **Se passou de uma hora, o plano de recuperação não é o backup — é
outro.**

Para restaurar **em produção**, pare `api` e `worker` antes (`docker compose stop api
worker`), restaure, e só então suba de novo — restaurar com a aplicação escrevendo
produz um estado inconsistente.

### Retenção

7 diários, 5 semanais (domingo) e 6 mensais (dia 1º), aplicada pelo próprio `backup.sh`
depois do envio. O que sai da regra é apagado do destino remoto.

O script também **confere o dump antes de enviar**: tamanho mínimo e `pg_restore --list`.
Sem isso, um erro dentro do container — banco fora do ar, disco cheio — geraria um arquivo
vazio que subiria todo dia como se fosse backup, e só apareceria no dia da restauração.

## Quando migrar para nuvem gerenciada

Gatilho: a operação do VPS consumindo tempo que deveria ser de produto, ou um cliente
exigindo SLA e backup gerenciado. O caminho já está preparado — ver
[infraestrutura](../docs/arquitetura/infraestrutura-e-deploy.md#evolução-para-azure-quando-houver-tração).
