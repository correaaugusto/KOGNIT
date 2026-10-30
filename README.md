# KOGNIT API

API em **C# / .NET 10** do KOGNIT (Desafio 2, Gestor Inteligente de Pequenos Negócios, Integrativa SI 2026.2).

O dono do negócio **conecta o banco que escolher** (Open Finance, via Pluggy) e a API devolve, **sem ele digitar nada**:
- **Entrou / Saiu / Contas pagas / Pra mim:** cada transação do extrato classificada sozinha (Pix de cliente → venda; CEMIG → energia; DAS → impostos; Pix para o CPF do dono → retirada…);
- **Contas e boletos a pagar:** contas fixas **descobertas no extrato**, **fatura do cartão** vinda do banco, **baixa automática** quando o pagamento aparece; boletos avulsos pela **linha digitável**;
- **Previsão de caixa de 30 dias** com semáforo, **alertas** em português e simulador "E se…?";
- **Atualização automática:** a Pluggy avisa (webhook) quando há transação nova.

Front-end: o app React [caixa-claro](https://github.com/correaaugusto/caixa-claro). O guia de integração está em [`docs/FRONTEND.md`](docs/FRONTEND.md).

| Documento | Conteúdo |
|---|---|
| [`docs/DECISOES.md`](docs/DECISOES.md) | todas as decisões, com o porquê |
| [`docs/ARQUITETURA.md`](docs/ARQUITETURA.md) | como foi programado: camadas, fluxos, onde mudar cada regra |
| [`docs/FRONTEND.md`](docs/FRONTEND.md) | como o React consome a API (login, conectar banco, telas, exemplos de JSON) |
| [`docs/DEPLOY-RENDER.md`](docs/DEPLOY-RENDER.md) | publicar: banco no Supabase, servidor no Render (`render.yaml`) |

## Rodar localmente

Requisito: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Kognit.Api --urls http://localhost:5080
```

- Abra **http://localhost:5080/docs** para a documentação interativa (dá para testar todas as rotas).
- Em desenvolvimento a API já vem com uma chave JWT de teste e o **banco de demonstração** habilitado: crie uma conta em `POST /api/auth/registrar` e chame `POST /api/conexoes/demo`.
- Exemplos prontos de chamadas: [`src/Kognit.Api/Kognit.Api.http`](src/Kognit.Api/Kognit.Api.http).

### Testes

```bash
dotnet test
```

São 34 testes. Para rodá-los também num PostgreSQL de verdade, defina `KOGNIT_TESTE_POSTGRES="Host=localhost;Username=…;Password=…"`.
- **regras do domínio:** classificação, contas fixas, baixa automática, previsão, alertas, linha digitável;
- **API rodando em memória** com uma Pluggy simulada: fluxo completo, segurança e webhook.

## Conectar bancos reais (Pluggy)

1. Crie uma conta em [pluggy.ai](https://pluggy.ai) e copie o **Client ID** e o **Client Secret**. Uma conta em período de teste só conecta os bancos de teste da Pluggy.
2. Configure as variáveis de ambiente (ou `appsettings.Development.json` localmente):

| Variável | Para quê |
|---|---|
| `Pluggy__ClientId`, `Pluggy__ClientSecret` | credenciais da Pluggy (só no servidor) |
| `Pluggy__Sandbox` | `true` mostra os bancos de teste no widget |
| `Pluggy__UrlPublicaApi` | endereço público desta API (para o webhook). No Render, é preenchido sozinho com `RENDER_EXTERNAL_URL` |
| `Pluggy__SegredoWebhook` | texto aleatório longo; vai no endereço do webhook |
| `Jwt__Chave` | segredo para assinar os logins (mín. 20 caracteres) — **obrigatório em produção** (o Render gera sozinho) |
| `Cors__Origens__0` | endereço do front, ex.: `https://caixa-claro.vercel.app` |
| `BancoDeDados__Provedor` | `Sqlite` (padrão, desenvolvimento) ou `Postgres` (Supabase) |
| `ConnectionStrings__Padrao` | conexão do banco (padrão: SQLite `kognit.db`; no Supabase, a do *Session pooler*) |

3. No front, use o widget **Pluggy Connect** com o token de `POST /api/conexoes/token` e envie o `item.id` para `POST /api/conexoes` (passo a passo em `docs/FRONTEND.md`).

## Rotas

| Método | Rota | O que faz |
|---|---|---|
| POST | `/api/auth/registrar`, `/api/auth/entrar` | cadastro e login (JWT) |
| GET/PUT | `/api/usuario` | nome do negócio, CPF do dono, dinheiro vivo inicial |
| GET | `/api/conexoes/provedores` | Pluggy configurada? demonstração disponível? |
| POST | `/api/conexoes/token` | token para abrir o widget Pluggy Connect |
| POST | `/api/conexoes` | registra o banco conectado (confere que é do usuário) e sincroniza |
| POST | `/api/conexoes/demo` | conecta o banco de demonstração |
| GET | `/api/conexoes` | bancos conectados, status e saldos |
| POST | `/api/conexoes/{id}/sincronizar` | "atualizar agora" |
| DELETE | `/api/conexoes/{id}` | desconectar |
| GET | `/api/painel` | tela inicial: saldo, semáforo, hoje, mês, alertas, "Confira", últimas movimentações, próximas contas |
| GET | `/api/extrato?filtro=tudo\|entrou\|saiu\|contasPagas\|praMim` | extrato com filtros, período e paginação |
| GET | `/api/contas?status=abertas\|pagas\|todas` | contas a pagar + total em 30/60/90 dias |
| POST | `/api/contas`, `/api/contas/ler-boleto` | cadastrar boleto (lê valor e vencimento da linha digitável) |
| POST/DELETE | `/api/contas/{id}/pagar`, `/api/contas/{id}` | marcar como paga / apagar |
| GET | `/api/previsao?menosRetiradaMes=&maisEntradaMes=` | previsão de 30 dias e "E se…?" |
| GET | `/api/lancamentos/confira` | o que o app não reconheceu com segurança |
| PUT | `/api/lancamentos/{id}/classificacao` | "é pessoal" / "é do negócio" (vira regra) |
| POST/DELETE | `/api/lancamentos`, `/api/lancamentos/{id}` | dinheiro vivo |
| POST | `/api/conexoes/{id}/simular`, `/simular/historico`; DELETE `/api/conexoes/simulacoes` | modo apresentação |
| POST | `/api/webhooks/pluggy?segredo=` | avisos da Pluggy (transação nova etc.) |

## Publicar (Supabase + Render)

- **Banco:** o **Supabase** (PostgreSQL).
- **API:** roda no **Render**, a partir do [`render.yaml`](render.yaml) da raiz. No Render: **New → Blueprint →** repositório KOGNIT, e cole a conexão do Supabase, as chaves da Pluggy e o endereço do front.
- **Front:** o React continua na Vercel.

Cada `git push` na `main` publica uma nova versão. Ao iniciar, a API aplica as migrações no Supabase. As tabelas ficam no schema `kognit`, fora da API pública do Supabase.

Passo a passo completo em [`docs/DEPLOY-RENDER.md`](docs/DEPLOY-RENDER.md).

## Estrutura

```
src/Kognit.Dominio   regras de negócio puras (testáveis, sem banco nem HTTP)
src/Kognit.Api       controllers, EF Core, cliente Pluggy, sincronização em segundo plano
tests/Kognit.Tests   testes do domínio e da API
docs/                    decisões, arquitetura, integração com o front
```
