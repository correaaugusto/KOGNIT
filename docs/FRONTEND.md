# Integrando o front React com a Caixa Claro API

Guia para o app React (o [caixa-claro](https://github.com/correaaugusto/caixa-claro) ou outro) consumir esta API.

- **Documentação interativa:** com a API rodando, abra `http://localhost:5080/docs`.
- **Contrato OpenAPI:** `/openapi/v1.json`. Dá para gerar os tipos TypeScript com `npx openapi-typescript http://localhost:5080/openapi/v1.json -o src/api/tipos.ts`.

## 1. Configuração

```bash
# .env do front (Vite)
VITE_API_URL=http://localhost:5080
```

Na API, o endereço do front precisa estar em `Cors:Origens` (`http://localhost:5173` já vem liberado).

## 2. Cliente HTTP com o token

```js
// src/api/cliente.js
const API = import.meta.env.VITE_API_URL;

export function salvarToken(token) { localStorage.setItem('caixa-claro:token', token); }
export function token() { return localStorage.getItem('caixa-claro:token'); }

export async function api(caminho, { metodo = 'GET', corpo } = {}) {
  const r = await fetch(`${API}${caminho}`, {
    method: metodo,
    headers: {
      ...(corpo ? { 'Content-Type': 'application/json' } : {}),
      ...(token() ? { Authorization: `Bearer ${token()}` } : {}),
    },
    body: corpo ? JSON.stringify(corpo) : undefined,
  });
  if (r.status === 401) { localStorage.removeItem('caixa-claro:token'); throw new Error('Entre de novo'); }
  if (r.status === 204 || r.status === 202) return null;
  const dados = await r.json();
  // Erros vêm no padrão ProblemDetails: { title, status, errors? }
  if (!r.ok) throw new Error(dados.title || 'Algo deu errado');
  return dados;
}
```

## 3. Login

```js
const { token, usuario } = await api('/api/auth/registrar', {
  metodo: 'POST',
  corpo: { nome, email, senha, nomeNegocio, cpf }, // cpf opcional: reconhece retiradas em conta PJ
});
salvarToken(token);

// depois: POST /api/auth/entrar { email, senha } → { token, expiraEm, usuario }
```

## 4. Conectar o banco (Pluggy Connect)

```bash
npm i react-pluggy-connect
```

```jsx
import { useEffect, useState } from 'react';
import { PluggyConnect } from 'react-pluggy-connect';
import { api } from './api/cliente';

export function ConectarBanco({ aoConectar }) {
  const [provedores, setProvedores] = useState(null);
  const [widget, setWidget] = useState(null); // { accessToken, incluirSandbox }
  const [erro, setErro] = useState('');

  useEffect(() => { api('/api/conexoes/provedores').then(setProvedores); }, []);

  async function conectar() {
    setWidget(await api('/api/conexoes/token', { metodo: 'POST', corpo: {} }));
  }

  if (!provedores) return null;
  return (
    <>
      {provedores.pluggyConfigurado && <button onClick={conectar}>Conectar meu banco</button>}
      {provedores.demoHabilitado && (
        <button onClick={async () => aoConectar(await api('/api/conexoes/demo', { metodo: 'POST' }))}>
          Usar banco de demonstração
        </button>
      )}
      {erro && <p>{erro}</p>}
      {widget && (
        <PluggyConnect
          connectToken={widget.accessToken}
          includeSandbox={widget.incluirSandbox}
          products={['ACCOUNTS', 'TRANSACTIONS', 'CREDIT_CARDS']}
          onSuccess={async ({ item }) => {
            setWidget(null);
            // A API confere que o banco foi conectado por este usuário e já sincroniza.
            aoConectar(await api('/api/conexoes', { metodo: 'POST', corpo: { itemId: item.id } }));
          }}
          onError={(e) => { setWidget(null); setErro(e?.message || 'A conexão não foi concluída'); }}
          onClose={() => setWidget(null)}
        />
      )}
    </>
  );
}
```

> Se a conta da Pluggy em período de teste falhar com `CREDIT_CARDS`, tire esse item de `products`. As faturas do cartão deixam de vir, mas o extrato continua.

**Reconectar** (o banco pediu nova autorização, `status: "erro"`):
1. Chame `POST /api/conexoes/token` com `{ itemId }` da conexão.
2. Abra o widget com `updateItem={itemId}`.

## 5. Telas → endpoints

| Tela | Chamada | Observação |
|---|---|---|
| Início | `GET /api/painel` | tudo numa chamada; a API atualiza o banco em segundo plano se fizer mais de 1 h |
| Extrato | `GET /api/extrato?filtro=tudo` | `filtro`: `tudo`, `entrou`, `saiu`, `contasPagas`, `praMim`; `de`, `ate` (AAAA-MM-DD, padrão: mês atual); `pagina`, `tamanho` |
| Contas | `GET /api/contas?status=abertas` | `status`: `abertas`, `pagas`, `todas`; traz `totalAPagar` (30/60/90 dias) |
| Previsão / "E se…?" | `GET /api/previsao?menosRetiradaMes=500&maisEntradaMes=0` | o slider só muda os parâmetros |
| Confira | `GET /api/lancamentos/confira` → `PUT /api/lancamentos/{id}/classificacao` `{ tipo, categoria? }` | "É pessoal" = `{ tipo: "retirada" }`; "É do negócio" = `{ tipo: "saida", categoria: "Fornecedor" }` |
| Dinheiro vivo | `POST /api/lancamentos` `{ tipo, valor, categoria?, data?, descricao? }` | o que passa pelo banco entra sozinho |
| Novo boleto | `POST /api/contas/ler-boleto` `{ linhaDigitavel }` → `POST /api/contas` `{ descricao, linhaDigitavel }` | valor e vencimento lidos da linha digitável |
| Atualizar agora | `POST /api/conexoes/{id}/sincronizar` (202) | depois, recarregue `/api/painel` após alguns segundos |
| Ajustes | `GET/PUT /api/usuario` | `nomeNegocio`, `cpf`, `caixaDinheiro` |
| Modo apresentação | `POST /api/conexoes/{id}/simular` `{ tipo, valor?, nome? }`, `POST /api/conexoes/{id}/simular/historico`, `DELETE /api/conexoes/simulacoes` | `tipo`: `pixRecebido`, `pixFornecedor`, `boletoPago`, `retirada`, `gastoPessoal` |

## 6. Exemplos reais de resposta

Capturados da API rodando com o banco de demonstração.

### `GET /api/painel` (trecho)
```json
{
  "nomeNegocio": "Pet Shop Amigo Fiel",
  "saldo": 3230.82,
  "semaforo": {
    "cor": "vermelho",
    "titulo": "Vai faltar dinheiro em 16/11",
    "texto": "No pior dia (20/11) o caixa fica em −R$ 450.",
    "menorSaldo": -450.0,
    "diaMenorSaldo": "2026-11-20",
    "primeiroDiaNegativo": "2026-11-16"
  },
  "hoje": { "entrou": 343.85, "saiu": 0, "sobrou": 343.85,
            "venceAmanha": [{ "descricao": "Boleto petiscos e areia", "valor": 420.0, "vencimento": "2026-10-30" }] },
  "mes": { "entrou": 7441.82, "saiu": 5195.49, "praMim": 3165.83, "lucro": 2246.33 },
  "alertas": [
    { "id": "faltar", "nivel": "alto", "titulo": "O caixa vai faltar",
      "texto": "No dia 16/11 o caixa pode ficar negativo. No pior dia (20/11) faltam R$ 450.",
      "sugestao": "Sugestão: reduzir suas retiradas em R$ 450 neste mês ou negociar o vencimento de \"Boleto ração (distribuidora)\" (R$ 1.650)." }
  ],
  "paraConferir": 6,
  "ultimasMovimentacoes": [
    { "id": "074213b4-…", "data": "2026-10-29", "valor": 243.52, "tipo": "entrada", "categoria": "Vendas",
      "descricao": "Stone Instituicao de Pagamento", "meio": "Maquininha", "origem": "banco",
      "motivo": "Recebimento de cliente", "revisar": false, "simulado": false, "contaPaga": null }
  ],
  "proximasContas": [
    { "id": "56aaee8a-…", "descricao": "Boleto petiscos e areia", "valor": 420.0, "categoria": "Fornecedor",
      "vencimento": "2026-10-30", "recorrente": false, "paga": false, "pagaEm": null, "pagaPeloBanco": false,
      "origem": "manual", "situacao": "proximos7Dias" }
  ],
  "conexoes": [
    { "id": "d3aac634-…", "provedor": "demo", "nomeBanco": "Banco de demonstração", "status": "conectado",
      "mensagem": null, "ultimaSincronizacao": "2026-10-30T01:09:48.3139564Z", "saldoContas": 3230.82,
      "contas": [{ "tipo": "BANK", "nome": "Conta corrente (simulada)", "saldo": 3230.82 },
                 { "tipo": "CREDIT", "nome": "Cartão Visa (simulado)", "saldo": 640.35 }] }
  ]
}
```

### `GET /api/extrato?filtro=contasPagas` (um item)
```json
{
  "totais": { "entrou": 7441.82, "saiu": 5195.49, "praMim": 3165.83, "lucro": 2246.33 },
  "movimentacoes": {
    "itens": [
      { "id": "bca42028-…", "data": "2026-10-20", "valor": 76.0, "tipo": "saida", "categoria": "Impostos",
        "descricao": "Das Simples Nacional", "meio": "Boleto", "origem": "banco", "motivo": "Reconhecido pelo nome",
        "revisar": false, "simulado": false,
        "contaPaga": { "id": null, "descricao": "Das Simples Nacional", "fixa": true } }
    ],
    "pagina": 1, "tamanhoPagina": 50, "total": 15
  }
}
```

### `POST /api/conexoes/{id}/simular` `{ "tipo": "pixRecebido", "valor": 150, "nome": "Maria" }`
```json
{ "ok": true, "titulo": "Entrou R$ 150,00: Maria",
  "linhas": ["Classificado sozinho como Venda: recebimento de cliente"],
  "lancamento": { "tipo": "entrada", "categoria": "Vendas", "meio": "Pix", "simulado": true, "…": "…" } }
```

### Erro (`ProblemDetails`)
```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.2", "title": "E-mail ou senha incorretos.", "status": 401 }
```

## 7. Valores dos campos (enums)

| Campo | Valores |
|---|---|
| `tipo` (lançamento) | `entrada`, `saida`, `retirada`, `aporte` |
| `origem` (lançamento) | `banco`, `manual` |
| `meio` | `Pix`, `Boleto`, `Débito automático`, `Cartão`, `Maquininha`, `Transferência`, `Dinheiro vivo` ou `null` |
| `semaforo.cor` | `verde`, `amarelo`, `vermelho` |
| `alertas[].nivel` | `alto`, `medio`, `info` |
| `contas[].origem` | `manual`, `detectada` (descoberta no extrato), `faturaCartao` |
| `contas[].situacao` | `atrasada`, `venceHoje`, `proximos7Dias`, `depois`, `paga` |
| `conexoes[].status` | `conectando`, `conectado`, `atualizando`, `erro` (mostre `mensagem`) |
| `conexoes[].provedor` | `pluggy`, `demo` |

## 8. Formatação no front

- Valores chegam como número (`1200.5`). Formate com `Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })`.
- Datas chegam como `"AAAA-MM-DD"`. Para não haver erro de fuso, **não** use `new Date("2026-10-29")` (ele interpreta como UTC). Separe ano, mês e dia.
- Horários (`ultimaSincronizacao`, `expiraEm`) chegam em UTC com "Z". `new Date(...)` converte para o horário local.
- Textos de alertas, semáforo e `motivo` já vêm prontos em português: mostre direto.
