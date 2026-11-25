# Greek NT Exegesis MCP Server

Servidor **Model Context Protocol (MCP)** em **C# / .NET 10 (LTS)** + **PostgreSQL 17 (`pg_trgm` + `unaccent`)** para exegese do Novo Testamento Grego com **zero alucinação**, 100% isolado via Docker Compose e pronto para deploy na **Oracle Cloud Always Free (OCI)**.

## Fontes de Dados Ingeridas (Abertas)

| Fonte | Conteúdo | Origem |
| :--- | :--- | :--- |
| **Família 35 (`f35` — Padrão)** | Texto Grego do NT segundo a Família 35 (*Wilbur N. Pickering*) com alinhamento determinístico conservador contra TAGNT | [eBible.org (`grcf35`)](https://ebible.org/find/details.php?id=grcf35) |
| **STEPBible `TAGNT`** | Texto amalgamado palavra por palavra (`TR`, `Byz`, `NA28`, `SBL`, `WH`, `Treg`, `Tyn`), Strong estendido e morfologia de Robinson | [STEPBible/STEPBible-Data](https://github.com/STEPBible/STEPBible-Data) (CC BY 4.0) |
| **STEPBible `TBESG`** | *Translators Brief Lexicon of Extended Strongs for Greek* | [STEPBible/STEPBible-Data](https://github.com/STEPBible/STEPBible-Data) (CC BY 4.0) |
| **STEPBible `TFLSJ`** | *Translators Formatted Full LSJ Bible Lexicon* (0–5624 + extra) | [STEPBible/STEPBible-Data](https://github.com/STEPBible/STEPBible-Data) (CC BY 4.0) |
| **Abbott-Smith** | *A Manual Greek Lexicon of the New Testament* (TEI XML: acepções, sinônimos e equivalentes LXX) | [translatable-exegetical-tools/Abbott-Smith](https://github.com/translatable-exegetical-tools/Abbott-Smith) |
| **Strong's Greek** | 5.624 verbetes originais de Strong com grego politônico, transliteração e etimologia | [morphgnt/strongs-dictionary-xml](https://github.com/morphgnt/strongs-dictionary-xml) |

---

## Tools MCP Expostas (`/mcp` via Streamable HTTP)

1. **`lookup_strong(code, detail_level="standard", lsj_offset=0, lsj_max_chars=12000)`**
   - `brief`: cabeçalho, contadores (F35/TR/Byz/NA28), TBESG e etimologia de Strong.
   - `standard` *(padrão)*: + definição de Strong + **Abbott-Smith** completo (acepções, sinônimos, equivalentes LXX) + resumo LSJ.
   - `full`: + **TFLSJ (LSJ)** integral paginado com segurança.
2. **`search_lemma(query, fuzzy=true, limit=15)`**
   - Busca por grego politônico ou sem diacríticos (`λόγος`, `λογος`, `αγαπη`), transliteração (`agape`) ou glosa em inglês (`love`), com fallback fuzzy `pg_trgm`.
3. **`word_occurrences(lemma_or_strong, edition="f35", book=null, limit=30, offset=0)`**
   - Concordância no NT com texto grego em contexto (forma destacada em **negrito**) e morfologia de Robinson decodificada em português. Edições: `f35` *(padrão)*, `tr`, `byz`, `na28`, `sbl`, `all`.
4. **`parse_verse(reference, edition="f35", include_variants=true)`**
   - Quebra versículo ou faixa curta (`Jo 1:1`, `João 1:1-5`, `1Co 13:4`, `1Jo 5:7-8`) palavra por palavra + bloco de comparação de variantes textuais (**F35**, **TR**, **Byz**, **NA28**).

---

## Execução Local (Docker Compose — Zero Instalação no Host)

```bash
cp .env.example .env

# 1. Subir o PostgreSQL 17
docker compose up -d db

# 2. Executar o ETL (baixa os datasets de commits fixados, parseia e carrega via COPY)
docker compose --profile etl run --rm etl

# 3. Subir o Servidor MCP (.NET 10 Streamable HTTP em http://localhost:8080/mcp)
docker compose up -d --build mcp-server

# 4. Verificar saúde e contagens do banco
curl http://localhost:8080/health
```

### Rodar Testes Unitários em Container

```bash
docker build -f Dockerfile.test -t greek-nt-mcp-tests . && docker run --rm greek-nt-mcp-tests
```

---

## Deploy na Oracle Cloud Always Free (OCI)

Os Dockerfiles são **multi-arquitetura** (`linux/arm64` para **Ampere A1 Flex** e `linux/amd64` para **E2.1.Micro**) com `icu-libs` habilitado para normalização completa de grego politônico no Alpine.

### 1. Pré-requisitos na OCI
- **Domínio / DDNS:** aponte um registro `A` (ex.: `exegesis.seudominio.com` ou DuckDNS gratuito) para o IP público reservado da VM.
- **VCN Security List (Ingress):** libere `80/tcp` e `443/tcp` (`0.0.0.0/0`), mantendo `5432` e `8080` fechados.
- **Firewall interno da VM Ubuntu/Oracle Linux:**
  ```bash
  sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80 -j ACCEPT
  sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
  sudo netfilter-persistent save
  ```

### 2. Configurar `.env` e Subir em Produção (com Caddy HTTPS Automático)

```bash
cp .env.example .env
chmod 600 .env
# Gere uma chave forte para MCP_API_KEY:
openssl rand -hex 32
```

Edite `.env` definindo `POSTGRES_PASSWORD`, `MCP_DOMAIN=seu-dominio.com` e `MCP_API_KEY=<token-gerado>`, e então execute:

```bash
# Subir PostgreSQL
docker compose -f docker-compose.yml -f docker-compose.oci.yml up -d db

# Popular o banco (ETL na própria VM)
docker compose -f docker-compose.yml -f docker-compose.oci.yml --profile etl run --rm etl

# Subir MCP Server + Caddy (TLS Let's Encrypt automático)
docker compose -f docker-compose.yml -f docker-compose.oci.yml up -d --build mcp-server caddy

# Verificar saúde via HTTPS
curl https://seu-dominio.com/health
```

### Backup rápido do PostgreSQL na OCI
```bash
docker compose exec -T db pg_dump -U exegesis greek_nt | gzip > backup_greek_nt_$(date +%F).sql.gz
```

---

## Configuração no Cliente MCP (Gemini CLI / Antigravity)

### Local (`http://localhost:8080/mcp`)
```json
{
  "mcpServers": {
    "greek-nt-exegesis": {
      "httpUrl": "http://localhost:8080/mcp"
    }
  }
}
```

### Produção na Oracle Cloud (`https://seu-dominio.com/mcp`)
```json
{
  "mcpServers": {
    "greek-nt-exegesis": {
      "httpUrl": "https://seu-dominio.com/mcp",
      "headers": {
        "Authorization": "Bearer SEU_MCP_API_KEY"
      }
    }
  }
}
```
