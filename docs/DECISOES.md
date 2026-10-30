# Decisões do projeto: Caixa Claro API

Cada decisão traz **o que foi decidido**, **por quê** e, quando houve, **as alternativas consideradas**.

Este repositório continua o projeto [caixa-claro](https://github.com/correaaugusto/caixa-claro) (Desafio 2, Integrativa SI 2026.2). As decisões de produto (público-alvo, foco em previsão de caixa, regras de alerta) estão no `docs/DECISOES.md` daquele repositório e **não foram alteradas**. Aqui ficam as decisões da API.

---

## 1. Objetivo

> "Construir uma API em C# que se integrará com um front-end em React. O usuário conecta com um banco real e tem todos aqueles retornos que simulamos: entrou, saiu, contas pagas, pra mim. Além do controle dos boletos que tem para pagar. Tudo isso apenas conectando com o banco que o usuário escolheu."

| Pedido | Onde está na API |
|---|---|
| Conectar com um banco real escolhido pelo usuário | `POST /api/conexoes/token` + widget Pluggy Connect + `POST /api/conexoes` |
| Entrou / Saiu / Contas pagas / Pra mim | `GET /api/extrato?filtro=entrou\|saiu\|contasPagas\|praMim` |
| Controle dos boletos a pagar | `GET /api/contas`: contas fixas **descobertas no extrato**, **fatura do cartão** vinda do banco e boletos cadastrados (com leitura da linha digitável) |
| Tela inicial, alertas, previsão | `GET /api/painel`, `GET /api/previsao` |
| Atualizar sozinho quando há transação nova | Webhook da Pluggy → fila em segundo plano (seção 6) |

---

## 2. Tecnologia

### A1. C# com .NET 10 e ASP.NET Core
- **Decisão:** .NET 10, a versão LTS atual, com suporte longo, e ASP.NET Core com **controllers**.
- **Por quê:**
  - A linguagem C# foi pedida.
  - Os controllers deixam cada recurso (conexões, extrato, contas…) num arquivo com rotas explícitas, um formato familiar em disciplinas de SI.
- **Alternativa:** *Minimal APIs* (rotas em `Program.cs`). São mais curtas, mas ficam menos organizadas quando o número de rotas cresce.

### A2. Três projetos: Domínio, Api e Tests
```
src/CaixaClaro.Dominio   regras puras (classificação, contas fixas, previsão, alertas)  — sem banco, sem HTTP
src/CaixaClaro.Api       controllers, banco de dados, Pluggy, segundo plano
tests/CaixaClaro.Tests   testes do domínio + testes da API rodando em memória
```
- **Por quê:**
  - As regras de negócio são o coração do produto e precisam ser testadas sem subir servidor nem banco.
  - Se um dia o agregador ou o banco de dados mudar, o Domínio não muda.
  - O projeto Domínio não referencia nada da Api (dependência numa direção só).

### A3. As regras foram portadas do app em JavaScript
- **Decisão:** a lógica validada no app React foi reescrita em C# com os mesmos nomes. Isso inclui `classificar`, `importarExtrato`, `detectarContasRecorrentes`, `conciliarContas`, `projecao`, `semaforo` e `alertas`.
- **Por quê:** o comportamento já tinha sido testado e mostrado. Manter os nomes facilita comparar as duas versões.
- **Bugs encontrados ao portar, já corrigidos aqui (seção 9):**
  - **Pagamento do mês passado quitava a conta deste mês.** A janela de "pagamento antecipado" (30 dias) alcançava o pagamento do ciclo anterior.
  - **Gasto repetido em meses não seguidos virava "conta fixa".** Exemplo: um conserto a cada 3 meses.

### A4. Dinheiro em `decimal`, datas em `DateOnly`
- `decimal` não tem erro de arredondamento binário; `double` tem (0,1 + 0,2 ≠ 0,3).
- `DateOnly` porque um lançamento é de **um dia**, sem hora. O "hoje" é sempre o de **Brasília** (`Relogio`), mesmo com o servidor em UTC.
- A data de cada transação é convertida para o fuso de Brasília. Assim, uma compra às 22h não cai no dia seguinte.

### A5. Formatação de reais própria
- Os textos dos alertas ("R$ 1.200") são formatados por código próprio (`Texto.ReaisCurto`), sem depender dos dados de idioma do sistema operacional.
- **Por quê:** imagens Docker enxutas às vezes não têm esses dados, e o texto sairia "R$1,200.00".

---

## 3. Banco de dados

### B1. EF Core + SQLite
- **Decisão:** Entity Framework Core com **SQLite** (um arquivo `caixaclaro.db`).
- **Por quê:** é zero configuração para rodar localmente e na apresentação. Trocar para **PostgreSQL** em produção é só mudar o provedor e a connection string (`UseNpgsql`), porque o resto do código usa EF.
- **Limitação:** SQLite guarda `decimal` como texto, então somar e ordenar valores direto no SQL não é confiável. Por isso as contas são feitas em memória (B2).

### B2. Estado do usuário carregado em memória
- **Decisão:** o `RepositorioEstado` carrega lançamentos, contas e regras do usuário para listas. As regras do Domínio trabalham nelas, e o repositório salva só as diferenças (novos, alterados e removidos).
- **Por quê:**
  - As regras (contas fixas, conciliação, previsão) cruzam muitos lançamentos entre si. Em memória ficam simples e iguais às testadas.
  - Um pequeno negócio tem alguns milhares de lançamentos por ano, o que cabe com folga.
- **Quando mudar:** com dezenas de milhares de lançamentos por usuário, levar os filtros do extrato para SQL (PostgreSQL) e carregar só a janela necessária.

### B3. `EnsureCreated` em vez de migrações
- **Decisão:** o banco é criado na primeira execução.
- **Por quê:** é um MVP, ainda sem dados em produção a preservar.
- **Antes de produção:** trocar por migrações do EF (`dotnet ef migrations add Inicial`), para alterar o esquema sem apagar dados.

### B4. Índices que protegem os dados
- `Lancamentos (UsuarioId, IdExterno)` **único**: a mesma transação do banco nunca entra duas vezes, mesmo com duas sincronizações ao mesmo tempo.
- `Conexoes.ItemId` **único**: um banco conectado pertence a um único usuário.
- Enums gravados como texto (`"Entrada"`, `"Detectada"`), para o banco ser legível.

---

## 4. Login e segurança

### S1. Login próprio com JWT
- **Decisão:**
  - `POST /api/auth/registrar` e `POST /api/auth/entrar` devolvem um **JWT** válido por 7 dias.
  - A senha é guardada com o `PasswordHasher` do ASP.NET Core (PBKDF2 com sal).
  - **Toda rota exige login** por padrão (`FallbackPolicy`). As exceções estão marcadas com `[AllowAnonymous]`: cadastro, login, webhook, documentação e `/saude`.
- **Por quê:** com dados bancários, login é obrigatório. Era a principal limitação do MVP em JavaScript.
- **Alternativas:** ASP.NET Identity completo (mais telas e tabelas do que o MVP precisa) ou um provedor externo (Auth0, Entra ID), que é uma boa evolução.

### S2. Cada consulta filtra pelo usuário do token
- O id do usuário vem **só do token** (`User.Id()`), nunca da URL ou do corpo.
- Um usuário que tenta acessar a conexão de outro recebe **404**, para não confirmar que ela existe (há teste para isso).

### S3. "Este banco é seu?": verificação pelo `clientUserId`
- **Decisão:**
  - Ao criar o token do widget, a API manda à Pluggy `clientUserId = id do usuário`.
  - Quando o front envia o `itemId` da conexão criada, a API **busca o item na Pluggy e confere** se o `clientUserId` é o do usuário logado. Se não for, devolve 403.
- **Por quê:** sem isso, quem descobrisse o `itemId` de outra pessoa poderia ler o extrato dela.

### S4. Outras proteções
- **Limite de tentativas** em login e cadastro: 10 por minuto por IP (`RateLimiter`).
- **Mesma mensagem** para e-mail inexistente e senha errada, para não revelar quem tem conta.
- **Erros no padrão `ProblemDetails`** (RFC 9110). Detalhes técnicos da Pluggy vão só para o log, nunca para o usuário.
- **CORS** só para as origens configuradas (`Cors:Origens`).
- **Segredos** (chave JWT, ClientSecret da Pluggy) só em variáveis de ambiente. A API **não sobe** se `Jwt:Chave` tiver menos de 32 caracteres.

### S5. LGPD (pendências antes de uso real)
- termo de consentimento e política de privacidade;
- opção de exportar e apagar todos os dados do usuário;
- criptografia do banco de dados em repouso (no PostgreSQL gerenciado é configuração do provedor).

---

## 5. Integração com o banco (Open Finance via Pluggy)

### P1. Pluggy como agregador
- **Decisão:** mantida a escolha do projeto original. Só instituições autorizadas pelo Banco Central acessam o Open Finance diretamente; a Pluggy é autorizada e tem ambiente de testes.
- **Já testado com o usuário:** a conexão com o banco de teste "Pluggy Bank Business" funcionou no app React.

### P2. Cliente HTTP próprio, com rotas conferidas no SDK oficial
- **Decisão:** **não existe SDK oficial da Pluggy em C#**, então foi escrito o `PluggyCliente` (HttpClient tipado).
- **Como garantimos que as rotas estão certas:** o site de documentação da Pluggy estava bloqueado neste ambiente. As rotas foram copiadas do **código-fonte do SDK oficial em Node** (`pluggy-sdk@0.94.0`), sem usar memória:

| Ação | Rota | Detalhe |
|---|---|---|
| Autenticar | `POST /auth` | `{ clientId, clientSecret, nonExpiring: false }` → `{ apiKey }` (JWT de curta duração) |
| Token do widget | `POST /connect_token` | `{ itemId, options: { clientUserId, webhookUrl, avoidDuplicates } }` → `{ accessToken }` |
| Item | `GET / PATCH / DELETE /items/{id}` | o PATCH pede atualização ao banco |
| Contas | `GET /accounts?itemId=` | `BANK` (corrente/poupança) e `CREDIT` (cartão) |
| Transações | `GET /v2/transactions?accountId=&dateFrom=` | paginação por cursor: segue o `next` até acabar (parâmetro `after`) |
| Faturas do cartão | `GET /bills?accountId=` | `dueDate`, `totalAmount` |
| Cabeçalho | `X-API-KEY: {apiKey}` | a chave é renovada antes de expirar (lida do campo `exp` do JWT) |

### P3. Provedores bancários atrás de uma interface
- **Decisão:** `IProvedorBancario` tem duas implementações: `ProvedorPluggy` (real) e `ProvedorDemo` (extrato simulado).
- **Por quê:**
  - O front e a apresentação funcionam **sem chaves da Pluggy**.
  - Trocar ou adicionar outro agregador (ex.: Belvo) não mexe no resto do sistema.

### P4. O que entra como movimento de caixa
- **Decisão:** só as transações da **conta corrente/poupança** viram lançamentos. As compras no **cartão** não entram uma a uma: entram pela **fatura**, como conta a pagar.
- **Por quê:** a compra no cartão só sai do caixa quando a fatura é paga. Importar as compras e também o pagamento da fatura **contaria o mesmo dinheiro duas vezes**.
- **Consequência:** o pagamento de fatura ("PAGAMENTO FATURA…") é classificado como "Fatura do cartão" e dá baixa na conta da fatura.

### P5. Saldo
- **Decisão:** saldo = **saldo informado pelo banco** (soma das contas `BANK`) + dinheiro vivo inicial + lançamentos anotados à mão + movimentos simulados "ao vivo".
- **Por quê:** o saldo do banco é a fonte da verdade. No app JavaScript o saldo era reconstruído a partir do extrato; aqui ficou mais simples e exato.

---

## 6. Atualização automática

### U1. Webhook da Pluggy → fila em segundo plano
- **Decisão:** a Pluggy chama `POST /api/webhooks/pluggy?segredo=…` quando há novidade.
- A API só **anota na fila qual conexão** sincronizar e responde 200 na hora. Um `BackgroundService` (`TrabalhadorSincronizacao`) faz a sincronização.
- **Eventos tratados:**
  - `transactions/created`, `transactions/updated`, `item/updated` e `item/created` → sincronizar;
  - `transactions/deleted` → apagar os lançamentos com aqueles ids;
  - `item/error`, `item/waiting_user_action` e `item/deleted` → marcar a conexão com erro e mensagem para reconectar.
- **Por quê:** é o mais perto de "a cada transação" que o Open Finance permite. A Pluggy espera resposta rápida, e a sincronização pode levar segundos.

### U2. Segurança do webhook
- O endereço do webhook leva um **segredo** (`Pluggy:SegredoWebhook`), comparado em tempo constante.
- O **conteúdo do aviso nunca é usado como dado**: ele só diz *qual* conexão sincronizar. Os dados são buscados de novo direto na Pluggy, com a chave do servidor.
- **Item desconhecido:** a API responde 200, para a Pluggy não ficar reenviando.

### U3. Redes de segurança
- **Ao abrir o app** (`GET /api/painel`): se a última sincronização tiver mais de 1 h, a API pede uma nova em segundo plano. A resposta não espera.
- **Rotina periódica** (`SincronizacaoPeriodica`): a cada 6 h (configurável) sincroniza todas as conexões, caso algum webhook se perca.
- **"Atualizar agora"** (`POST /api/conexoes/{id}/sincronizar`): pede dados novos ao banco (PATCH no item). A fila tenta de novo a cada 5 s enquanto o item estiver `UPDATING`, por até ~2 min.
- **Pedidos repetidos** para a mesma conexão viram uma sincronização só.

### U4. Uma alteração por vez por usuário
- **Decisão:** `TravaPorUsuario` (um semáforo por usuário) envolve a sincronização e toda ação que altera dados.
- **Por quê:** sem isso, um webhook e um "Confira" ao mesmo tempo poderiam salvar versões diferentes.
- **Limite:** vale para **um servidor**. Com vários servidores, trocar por trava no banco de dados (ex.: `SELECT … FOR UPDATE` no PostgreSQL) ou por uma fila externa.

### U5. Janela de busca
- **Primeira sincronização:** últimos **120 dias**.
- **Depois:** a partir da última sincronização **menos 7 dias**, porque bancos confirmam transações com atraso. O índice único (B4) impede duplicatas.
- **Transações `PENDING`** são ignoradas até serem confirmadas.

---

## 7. Boletos e contas a pagar

### C1. O que vem "só conectando o banco"
| Fonte | Como |
|---|---|
| **Contas fixas descobertas** | Mesmo favorecido, 1 vez por mês, em **meses seguidos**, valor ±20% e dia parecido. O app cria a próxima conta com vencimento previsto (aluguel, luz, água, internet, DAS…). |
| **Fatura do cartão** | Da lista de faturas do cartão (`/bills`). Se o banco não informar, usa o saldo e o vencimento da conta de crédito. |
| **Baixa automática** | Quando o pagamento aparece no extrato: mesmo favorecido (até 30 dias antes, mas depois do vencimento anterior, e até 10 dias depois), valor exato (±10 dias) ou "PAGAMENTO FATURA" para o cartão. |

### C2. Limite honesto: boletos avulsos ainda não emitidos
- **O que verificamos:** nos tipos do SDK oficial da Pluggy há contas, transações, cartões, faturas, empréstimos, investimentos e a emissão de boletos para **receber**. **Não há nada para listar os boletos registrados no CPF/CNPJ que ainda não foram pagos** (o "DDA" do internet banking). Vale confirmar com a Pluggy se existe um produto para isso.
- **Decisão:** para esses boletos, a API aceita a **linha digitável** (`POST /api/contas` com `linhaDigitavel`) e **lê sozinha valor e vencimento**. Se o pagamento aparecer depois no extrato, a baixa é automática.
- **Leitura da linha digitável:**
  - boleto bancário de 47 dígitos: dígitos verificadores módulo 10; fator de vencimento com a virada de 22/02/2025 (fator 1000);
  - conta de consumo ou tributo de 48 dígitos: valor, mas sem vencimento padronizado.
- **Evolução:** ler o código de barras pela câmera do celular no front, e DDA via parceiro bancário.

---

## 8. Formato da API para o React

### F1. Um endpoint por tela
| Tela | Endpoint |
|---|---|
| Início | `GET /api/painel`: tudo numa chamada (saldo, semáforo, resumo de hoje, mês, alertas, "Confira", últimas movimentações, próximas contas, conexões) |
| Extrato | `GET /api/extrato?filtro=…&de=…&ate=…&pagina=…` |
| Contas | `GET /api/contas?status=abertas\|pagas\|todas` |
| Previsão | `GET /api/previsao?menosRetiradaMes=…&maisEntradaMes=…` (o simulador "E se…?" é só mudar os parâmetros) |

- **Por quê:** menos chamadas no celular e menos lógica no front. As regras ficam todas no servidor.

### F2. JSON em camelCase; enums como texto
- Exemplos: `"tipo": "entrada"`, `"cor": "vermelho"`, `"origem": "faturaCartao"`.
- **Datas:** `"2026-10-29"`. **Valores:** número (`1200.5`); o front formata.
- **Textos prontos em português** para mostrar direto: títulos de alerta, "motivo" de cada classificação, semáforo.

### F3. Documentação interativa
- `/docs` (Scalar) e `/openapi/v1.json`.
- O front pode gerar tipos TypeScript a partir do OpenAPI (ex.: `openapi-typescript`).

---

## 9. Bugs encontrados e corrigidos durante a construção

| Bug | Como apareceu | Correção |
|---|---|---|
| Pagamento do mês passado quitava a conta fixa deste mês | Teste de integração: a conta da CEMIG saiu "paga" com o pagamento de 30 dias atrás | Com o mesmo favorecido, o pagamento tem que ser **depois do vencimento do mês anterior** (teste `Pagamento_do_mes_passado_nao_quita_a_conta_fixa_deste_mes`) |
| Gasto em meses não seguidos virava conta fixa | Teste manual: "Assistência técnica" (a cada 3 meses) apareceu como conta mensal | A detecção exige **meses seguidos** (teste `Gasto_que_se_repete_em_meses_nao_seguidos_nao_vira_conta_fixa`) |
| Horários saíam sem fuso no JSON (`…T01:09:48`, sem "Z") | Teste manual da API: o SQLite devolve datas sem fuso, e o React leria como horário local | Toda data é gravada e lida como **UTC** (`ConfigureConventions` no `AppDbContext`); o teste confere o "Z" |
| Extrato de demonstração mudava conforme o dia | Revisão: a semente era a data de hoje, e os ids se repetiam com valores diferentes no dia seguinte | Semente e id **por dia da transação** (`demo-AAAAMMDD-n`) |

---

## 10. Testes

- **33 testes automatizados** (`dotnet test`).
- **Domínio:** classificação, contas fixas, baixa automática (inclusive fatura e pagamento manual trocado pelo do banco), "Confira", previsão, semáforo, alertas, linha digitável e extrato de demonstração.
- **API rodando em memória** (`WebApplicationFactory`) com banco SQLite temporário e uma **Pluggy falsa** que responde nas mesmas rotas e formatos do SDK oficial. Cobrem:
  - fluxo completo: cadastro → token do widget → conectar → painel, extrato (entrou, saiu, contas pagas, pra mim), contas (conta fixa + fatura), "Confira", previsão;
  - segurança: sem login → 401; banco de outra pessoa → 403; dados de outro usuário → 404 ou vazio;
  - webhook: segredo errado → 401; transação nova chega sozinha ao extrato;
  - dinheiro vivo, linha digitável e modo apresentação.

---

## 11. Hospedagem

- **A Vercel não roda .NET.** O front React continua na Vercel; a API vai para um serviço com **Docker** (há `Dockerfile`): Render, Railway, Fly.io ou Azure App Service.
- **Variáveis de ambiente obrigatórias:** `Jwt__Chave`, `Pluggy__ClientId`, `Pluggy__ClientSecret`, `Pluggy__UrlPublicaApi`, `Pluggy__SegredoWebhook` e `Cors__Origens__0` (endereço do front na Vercel).
- **SQLite:** o arquivo fica no volume `/data`. Para mais de um servidor, migrar para PostgreSQL (B1, U4).

---

## 12. Próximos passos

1. **Integrar o front React** a esta API (guia em `docs/FRONTEND.md`), substituindo o `localStorage` e as funções `/api` da Vercel.
2. **Notificação push** no celular quando o webhook trouxer transação nova (Web Push com VAPID).
3. **Migrações do EF e PostgreSQL** antes de dados reais.
4. **LGPD** (seção S5) e **produção na Pluggy** (a conta em teste só conecta bancos de teste).
5. **Leitura do código de barras pela câmera** e **DDA** para boletos avulsos.
