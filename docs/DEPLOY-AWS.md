# Publicar a KOGNIT API: Supabase (banco) + AWS (servidor)

```
Front (Vercel) ──HTTPS──▶ AWS: Caddy (HTTPS) ─▶ KOGNIT API (Docker)
                                                   │
                                                   └──SSL──▶ Supabase (PostgreSQL)
Pluggy ──webhook HTTPS──▶ AWS
```

Os dados ficam no **Supabase**, com backup do próprio Supabase. O servidor na AWS **não guarda nada**: pode ser recriado ou atualizado sem perder dados.

As escolhas e os motivos estão em `DECISOES.md`, seção 13.

---

## Parte 1: Supabase

1. **Criar o projeto** em [supabase.com](https://supabase.com) → *New project*:
   - **Região:** *South America (São Paulo)*, a mesma da AWS mais adiante (menos latência e dados no Brasil).
   - Defina uma **senha do banco** forte e guarde-a.

2. **Pegar a conexão:** botão **Connect**, no topo do painel → **Session pooler**.
   - Use o *Session pooler*, e não a *Direct connection*. A conexão direta do Supabase usa IPv6, e a rede padrão da AWS é IPv4.
   - Use o modo *Session* (porta 5432), e não o *Transaction* (porta 6543).
   - Monte a connection string no formato .NET (Npgsql), copiando **host** e **usuário** exatamente como aparecem no painel:

   ```
   Host=aws-0-sa-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.SEU_PROJECT_REF;Password=SUA_SENHA;SSL Mode=Require;Maximum Pool Size=10
   ```

   - `SSL Mode=Require`: a conexão vai criptografada.
   - `Maximum Pool Size=10`: não esgota as conexões do pooler, que são limitadas por plano.

3. **Não é preciso criar tabelas.** Ao iniciar, a API aplica as **migrações** sozinha:
   - cria o schema `kognit` e as tabelas;
   - remove o acesso dos papéis públicos do Supabase (`anon` e `authenticated`).

   Se preferir rodar o SQL à mão (no *SQL Editor* do Supabase), gere o script com `dotnet ef migrations script --idempotent -p src/Kognit.Api`.

4. **Não exponha o schema `kognit`.** Em *Project Settings → Data API*, não adicione `kognit` aos *Exposed schemas*. Assim a API REST automática do Supabase não alcança os dados financeiros. A KOGNIT não usa essa API: só o servidor, com o usuário do banco, lê e grava.

> **Plano gratuito do Supabase:** confira no site os limites atuais (tamanho do banco, conexões, backups). Projetos sem uso podem ser **pausados**. Para a apresentação, abra o painel um dia antes.

---

## Parte 2: AWS, servidor (EC2 + Docker): o caminho mais simples

### 2.1 Criar a máquina
Console AWS → **EC2** → *Launch instance*:

| Campo | Valor |
|---|---|
| Região (canto superior direito) | **South America (São Paulo) sa-east-1** |
| Nome | `kognit-api` |
| Imagem | **Ubuntu Server 24.04 LTS** |
| Tipo | `t3.micro` ou `t3.small` (a API usa pouca memória; contas novas podem ter nível gratuito, confira no console) |
| Par de chaves | crie um (ex.: `kognit`) e baixe o `.pem` |
| Rede / *Security group* | liberar **SSH (22) só do seu IP**, **HTTP (80)** e **HTTPS (443)** de qualquer lugar |
| Armazenamento | 20 GB (padrão serve) |

Depois:
- **EC2 → Elastic IPs → Allocate** → *Associate* à instância. É um IP fixo, que não muda ao reiniciar.
- **Domínio:**
  - crie um registro **A** (ex.: `api.seudominio.com.br`) apontando para o Elastic IP;
  - **sem domínio, para testar:** use `IP-COM-HIFENS.sslip.io` (ex.: `54-12-34-56.sslip.io`), que já aponta para o seu IP.

### 2.2 Preparar a máquina

```bash
ssh -i kognit.pem ubuntu@SEU_ELASTIC_IP
```

```bash
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-v2 git
```

```bash
sudo usermod -aG docker ubuntu && newgrp docker
```

### 2.3 Baixar o código e configurar
O repositório é privado? Use um *Personal Access Token* do GitHub (permissão de leitura) no lugar da senha.

```bash
git clone https://github.com/correaaugusto/KOGNIT.git && cd KOGNIT
```

```bash
cp .env.exemplo .env && nano .env
```

Preencha no `.env`:
- `DOMINIO`;
- a connection string do Supabase;
- `Jwt__Chave` e `Pluggy__SegredoWebhook` (gere cada um com `openssl rand -base64 48`);
- as chaves da Pluggy;
- `Pluggy__UrlPublicaApi=https://SEU_DOMINIO`;
- o endereço do front em `Cors__Origens__0`.

### 2.4 Subir

```bash
docker compose up -d --build
```

O que acontece:
1. O Docker compila a API.
2. O Caddy pede o certificado HTTPS para o seu domínio (leva alguns segundos).
3. A API aplica as migrações no Supabase e começa a responder.

```bash
curl https://SEU_DOMINIO/saude
```

Deve responder `{"status":"ok"}`. A documentação interativa fica em `https://SEU_DOMINIO/docs`.

**Logs:** `docker compose logs -f api`

### 2.5 Atualizar depois de mudanças no código

```bash
cd KOGNIT && git pull && docker compose up -d --build
```

### 2.6 Ligar o resto
- **Front (Vercel):** `VITE_API_URL=https://SEU_DOMINIO`, e faça um novo deploy.
- **Pluggy:** nada a configurar no painel. A API informa o endereço do webhook a cada conexão, usando `Pluggy__UrlPublicaApi` e o segredo.

### 2.7 Cuidados
- **Billing → Budgets:** crie um alerta de gastos (ex.: US$ 10).
- **Segredos:** o `.env` fica só na máquina. Para algo mais robusto, use o *AWS Systems Manager Parameter Store*.
- **Manutenção:** `sudo apt-get upgrade` de tempos em tempos.

---

## Parte 3: quando crescer (produção gerenciada)

Com o banco no Supabase, a API é "sem estado" e pode rodar em contêiner gerenciado:

| Peça | Serviço AWS |
|---|---|
| Imagem Docker | **ECR** (`docker build` + `docker push`) |
| Executar a API | **ECS Fargate** (1+ tarefas) |
| HTTPS e domínio | **Application Load Balancer** + certificado gratuito do **ACM** |
| Segredos | **Secrets Manager** → variáveis da tarefa |
| Logs | **CloudWatch Logs** |

**Antes de passar de 1 para várias tarefas:**
1. Trocar a `TravaPorUsuario` (vale para um servidor só) por uma trava no banco de dados (ver `DECISOES.md`, U4).
2. Deixar a rotina periódica de sincronização em só uma tarefa.

Atrás do ALB, mantenha `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, para o limite de tentativas de login usar o IP real.

---

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| A API não sobe: "Configure Jwt:Chave…" | faltou `Jwt__Chave` (32+ caracteres) no `.env` |
| Erro de conexão com o banco / *timeout* | usou a *Direct connection* (IPv6) em vez do *Session pooler*; ou senha/usuário errados (o usuário do pooler é `postgres.SEU_PROJECT_REF`) |
| "too many connections" | baixe `Maximum Pool Size` na connection string |
| O Caddy não emite o certificado | o domínio ainda não aponta para o Elastic IP, ou as portas 80/443 não estão liberadas no *security group* |
| O front recebe erro de CORS | `Cors__Origens__0` diferente do endereço exato do front (com `https://`, sem barra no final) |
| O webhook não chega | `Pluggy__UrlPublicaApi` errado ou sem HTTPS |
