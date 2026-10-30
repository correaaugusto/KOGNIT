# Publicar a KOGNIT API: Supabase (banco) + Render (servidor)

```
Front (Vercel) ──HTTPS──▶ Render: KOGNIT API (Docker) ──SSL──▶ Supabase (PostgreSQL)
Pluggy ──webhook HTTPS──▶ Render
```

- **Supabase:** guarda os dados (PostgreSQL gerenciado).
- **Render:** roda a API. Ele constrói o `Dockerfile` do repositório, dá **HTTPS** e um endereço público, e **publica de novo a cada `git push`**.
- **O servidor não guarda nada:** pode ser recriado sem perder dados.

O arquivo [`render.yaml`](../render.yaml), na raiz, descreve o serviço inteiro (é um *Blueprint*). O Render lê esse arquivo e configura tudo sozinho. As escolhas e os motivos estão em `DECISOES.md`, seção 13.

---

## Antes de começar: a região

O Render tem servidores em **Virginia, Ohio, Oregon (EUA), Frankfurt e Singapura**. Não tem São Paulo.

Cada tela da API faz várias consultas ao banco. Por isso **banco e servidor precisam ficar na mesma região**, senão cada tela fica lenta.

| | Região |
|---|---|
| Render (`render.yaml`) | `virginia` |
| Supabase (ao criar o projeto) | **East US (North Virginia)** |

> **Já criou o projeto no Supabase em São Paulo?** Funciona, mas cada consulta atravessa o continente e as telas ficam mais lentas. Para o MVP, o ideal é criar o projeto em North Virginia. Sobre LGPD: guardar dados fora do Brasil é permitido com salvaguardas (transferência internacional). Para clientes reais, vale validar com o professor ou orientador.

---

## Parte 1: Supabase

1. Em [supabase.com](https://supabase.com) → **New project**:
   - **Região:** East US (North Virginia);
   - **senha do banco** forte. Guarde-a.

2. **Pegar a conexão:** botão **Connect** (topo do painel) → **Session pooler**.
   - Use o *Session pooler* (porta 5432), e não a *Direct connection* (IPv6) nem o *Transaction pooler* (6543).
   - Monte a connection string no formato .NET, copiando **host** e **usuário** exatamente como aparecem no painel:

   ```
   Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.SEU_PROJECT_REF;Password=SUA_SENHA;SSL Mode=Require;Maximum Pool Size=10
   ```

3. **Não precisa criar tabelas.** Se quiser criá-las à mão ou conferir a estrutura, o script completo está em [`database/kognit.sql`](../database/kognit.sql) (Supabase → *SQL Editor* → colar → *Run*; pode rodar mais de uma vez). De qualquer forma, ao iniciar, a API aplica as migrações sozinha:
   - cria o schema `kognit` e as tabelas;
   - tira o acesso dos papéis públicos do Supabase (`anon` e `authenticated`).

4. **Não exponha o schema `kognit`.** Em *Project Settings → Data API*, não adicione `kognit` aos *Exposed schemas*. A KOGNIT não usa a API REST automática do Supabase: só a nossa API lê e grava.

---

## Parte 2: Render

1. Crie a conta em [render.com](https://render.com) entrando com o **GitHub**, e autorize o acesso ao repositório **KOGNIT**.

2. **New → Blueprint** → escolha o repositório **KOGNIT** (branch `main`). O Render encontra o `render.yaml` e mostra o serviço `kognit-api`.

3. **Preencha os valores que o Render pedir** (os marcados com `sync: false` no `render.yaml`):

| Variável | O que colar |
|---|---|
| `ConnectionStrings__Padrao` | a connection string do Supabase (Parte 1, passo 2) |
| `Pluggy__ClientId` / `Pluggy__ClientSecret` | as credenciais da Pluggy (*Dashboard → API Keys*) |
| `Cors__Origens__0` | o endereço do front, ex.: `https://kognit.vercel.app` (com `https://`, sem barra no final) |

   Já vêm prontos e **não precisam de nada**:
   - `Jwt__Chave` e `Pluggy__SegredoWebhook`: gerados aleatoriamente pelo Render;
   - provedor Postgres, modo de produção, sandbox da Pluggy, IP real atrás do proxy.

4. **Apply.** O Render constrói a imagem (a primeira vez leva alguns minutos) e sobe a API. Ao iniciar, a API cria as tabelas no Supabase.

5. **Conferir:** no painel do serviço aparece o endereço, algo como `https://kognit-api.onrender.com`.
   - `https://…onrender.com/saude` deve responder `{"status":"ok"}`;
   - `https://…onrender.com/docs` mostra a documentação interativa.

6. **Ligar o front:** na Vercel, `VITE_API_URL=https://kognit-api.onrender.com`, e faça um novo deploy.

**Pluggy:** não precisa configurar nada no painel dela. A cada conexão, a API informa o endereço do webhook. Esse endereço é montado com o endereço público do Render (variável `RENDER_EXTERNAL_URL`, fornecida pelo Render) e com o segredo gerado.
- Se nos logs a API avisar que não há endereço público, crie a variável `Pluggy__UrlPublicaApi` com o endereço `https://…onrender.com`.

---

## Atualizar

Basta `git push` na `main`. O Render publica a nova versão sozinho, e as migrações novas são aplicadas ao iniciar.

## Plano gratuito ou pago

| | `free` (padrão no `render.yaml`) | `starter` (pago) |
|---|---|---|
| Custo | gratuito | mensalidade baixa (confira na página de preços do Render) |
| Hiberna sem uso | **sim**: o primeiro acesso depois de um tempo parado demora para responder | não |
| Webhook da Pluggy e sincronização em segundo plano | só funcionam com o serviço acordado | sempre |

- **Para a apresentação, o `free` serve:** abra `/saude` alguns minutos antes, para "acordar" a API.
- **Com clientes reais, use o `starter`:** troque `plan: free` por `plan: starter` no `render.yaml` e faça `git push`.
- Os limites exatos do plano gratuito mudam com o tempo: confira em render.com/pricing.

---

## Rodar na sua máquina apontando para o Supabase (opcional)

```bash
cp .env.exemplo .env
```

```bash
set -a && source .env && set +a && dotnet run --project src/Kognit.Api --urls http://localhost:5080
```

---

## Problemas comuns

| Sintoma | Causa provável |
|---|---|
| O deploy falha com "Configure Jwt:Chave…" | `Jwt__Chave` vazio (o Blueprint gera sozinho; se o serviço foi criado à mão, crie a variável com 20+ caracteres) |
| A API não sobe: erro de conexão / *timeout* com o banco | connection string errada: use o *Session pooler*, e o usuário é `postgres.SEU_PROJECT_REF` |
| "too many connections" | baixe `Maximum Pool Size` na connection string |
| As telas demoram vários segundos | banco e servidor em regiões diferentes; ou o plano `free` acabou de acordar |
| O front recebe erro de CORS | `Cors__Origens__0` diferente do endereço exato do front |
| O webhook não chega | serviço hibernando (plano `free`) ou endereço público ausente (crie `Pluggy__UrlPublicaApi`) |
