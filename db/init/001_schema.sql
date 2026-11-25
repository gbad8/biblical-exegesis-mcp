-- Greek NT Exegesis MCP Server — PostgreSQL 17 Schema
-- Extensions for fuzzy trigram search and accent-insensitive ASCII transliteration search
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

-- 1. NT Books catalog (27 books with PT/EN aliases)
CREATE TABLE IF NOT EXISTS bible_books (
    book_id     SMALLINT PRIMARY KEY,
    osis_code   VARCHAR(8)  NOT NULL UNIQUE,
    usfm_code   VARCHAR(8)  NOT NULL UNIQUE,
    name_en     TEXT        NOT NULL,
    name_pt     TEXT        NOT NULL,
    aliases     TEXT[]      NOT NULL
);

-- 2. Master lexicon entries (Strong's G0001-G5624 + STEPBible TBESG extended Strongs)
CREATE TABLE IF NOT EXISTS lexicon_entries (
    strong_code                 VARCHAR(24) PRIMARY KEY,
    base_strong_code            VARCHAR(12) NOT NULL,
    strong_number               INTEGER,
    is_extended                 BOOLEAN     NOT NULL DEFAULT FALSE,
    lemma                       TEXT        NOT NULL,
    lemma_normalized            TEXT        NOT NULL,
    transliteration             TEXT,
    transliteration_normalized  TEXT,
    pos_code                    VARCHAR(64),
    short_gloss                 TEXT,
    tbesg_meaning               TEXT,
    strong_derivation           TEXT,
    strong_definition           TEXT,
    strong_kjv_definition       TEXT,
    occurrences_f35             INTEGER     NOT NULL DEFAULT 0,
    occurrences_tr              INTEGER     NOT NULL DEFAULT 0,
    occurrences_byz             INTEGER     NOT NULL DEFAULT 0,
    occurrences_na28            INTEGER     NOT NULL DEFAULT 0,
    search_vector               TSVECTOR
);

CREATE INDEX IF NOT EXISTS idx_lexicon_base_strong ON lexicon_entries (base_strong_code);
CREATE INDEX IF NOT EXISTS idx_lexicon_strong_number ON lexicon_entries (strong_number);
CREATE INDEX IF NOT EXISTS idx_lexicon_lemma_norm ON lexicon_entries (lemma_normalized);
CREATE INDEX IF NOT EXISTS idx_lexicon_translit_norm ON lexicon_entries (transliteration_normalized);
CREATE INDEX IF NOT EXISTS idx_lexicon_lemma_trgm ON lexicon_entries USING GIN (lemma_normalized gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_lexicon_translit_trgm ON lexicon_entries USING GIN (transliteration_normalized gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_lexicon_gloss_trgm ON lexicon_entries USING GIN (short_gloss gin_trgm_ops);
CREATE INDEX IF NOT EXISTS idx_lexicon_search_vector ON lexicon_entries USING GIN (search_vector);

-- 3. Abbott-Smith Manual Greek Lexicon (TEI XML)
CREATE TABLE IF NOT EXISTS lexicon_abbott_smith (
    id                  SERIAL PRIMARY KEY,
    strong_code         VARCHAR(12),
    headword            TEXT    NOT NULL,
    headword_normalized TEXT    NOT NULL,
    grammatical_info    TEXT,
    etymology           TEXT,
    lxx_equivalents     TEXT[]  NOT NULL DEFAULT '{}',
    synonyms            TEXT[]  NOT NULL DEFAULT '{}',
    senses_json         JSONB,
    full_markdown       TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_abbott_strong ON lexicon_abbott_smith (strong_code);
CREATE INDEX IF NOT EXISTS idx_abbott_headword_norm ON lexicon_abbott_smith (headword_normalized);

-- 4. Liddell-Scott-Jones (STEPBible TFLSJ 0-5624 + extra)
CREATE TABLE IF NOT EXISTS lexicon_lsj (
    id                  SERIAL PRIMARY KEY,
    strong_code         VARCHAR(24) NOT NULL,
    base_strong_code    VARCHAR(12) NOT NULL,
    headword            TEXT,
    headword_normalized TEXT,
    summary_markdown    TEXT,
    full_markdown       TEXT        NOT NULL,
    char_length         INTEGER     NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_lsj_strong ON lexicon_lsj (strong_code);
CREATE INDEX IF NOT EXISTS idx_lsj_base_strong ON lexicon_lsj (base_strong_code);
CREATE INDEX IF NOT EXISTS idx_lsj_headword_norm ON lexicon_lsj (headword_normalized);

-- 5. TAGNT Amalgamated Greek NT tokens (TR, Byz, NA28, SBL, WH, Treg, Tyn)
CREATE TABLE IF NOT EXISTS nt_tokens (
    id                      BIGSERIAL PRIMARY KEY,
    book_id                 SMALLINT    NOT NULL REFERENCES bible_books(book_id),
    chapter                 SMALLINT    NOT NULL,
    verse                   SMALLINT    NOT NULL,
    word_position           SMALLINT    NOT NULL,
    sub_position            SMALLINT    NOT NULL DEFAULT 0,
    surface_form            TEXT        NOT NULL,
    surface_normalized      TEXT        NOT NULL,
    punctuation_before      TEXT,
    punctuation_after       TEXT,
    transliteration         TEXT,
    contextual_gloss        TEXT,
    strong_code             VARCHAR(24),
    base_strong_code        VARCHAR(12),
    secondary_strong_codes  VARCHAR(24)[] NOT NULL DEFAULT '{}',
    lemma                   TEXT,
    lemma_normalized        TEXT,
    morph_code              VARCHAR(64),
    edition_group           VARCHAR(16),
    editions_raw            TEXT,
    in_tr                   BOOLEAN     NOT NULL DEFAULT FALSE,
    in_byz                  BOOLEAN     NOT NULL DEFAULT FALSE,
    in_na28                 BOOLEAN     NOT NULL DEFAULT FALSE,
    in_sbl                  BOOLEAN     NOT NULL DEFAULT FALSE,
    variant_note            TEXT
);

CREATE INDEX IF NOT EXISTS idx_nt_tokens_ref ON nt_tokens (book_id, chapter, verse, word_position, sub_position);
CREATE INDEX IF NOT EXISTS idx_nt_tokens_base_strong ON nt_tokens (base_strong_code);
CREATE INDEX IF NOT EXISTS idx_nt_tokens_strong ON nt_tokens (strong_code);
CREATE INDEX IF NOT EXISTS idx_nt_tokens_lemma_norm ON nt_tokens (lemma_normalized);
CREATE INDEX IF NOT EXISTS idx_nt_tokens_secondary_strongs ON nt_tokens USING GIN (secondary_strong_codes);
CREATE INDEX IF NOT EXISTS idx_nt_tokens_tr_strong ON nt_tokens (base_strong_code) WHERE in_tr = TRUE;
CREATE INDEX IF NOT EXISTS idx_nt_tokens_byz_strong ON nt_tokens (base_strong_code) WHERE in_byz = TRUE;
CREATE INDEX IF NOT EXISTS idx_nt_tokens_na28_strong ON nt_tokens (base_strong_code) WHERE in_na28 = TRUE;

-- 6. Family 35 (Wilbur Pickering / eBible grcf35) faithful verses + conservatively aligned tokens
CREATE TABLE IF NOT EXISTS f35_verses (
    book_id     SMALLINT NOT NULL REFERENCES bible_books(book_id),
    chapter     SMALLINT NOT NULL,
    verse       SMALLINT NOT NULL,
    verse_text  TEXT     NOT NULL,
    PRIMARY KEY (book_id, chapter, verse)
);

CREATE TABLE IF NOT EXISTS f35_tokens (
    id                      BIGSERIAL PRIMARY KEY,
    book_id                 SMALLINT    NOT NULL REFERENCES bible_books(book_id),
    chapter                 SMALLINT    NOT NULL,
    verse                   SMALLINT    NOT NULL,
    word_position           SMALLINT    NOT NULL,
    surface_form            TEXT        NOT NULL,
    surface_normalized      TEXT        NOT NULL,
    punctuation_before      TEXT,
    punctuation_after       TEXT,
    transliteration         TEXT,
    contextual_gloss        TEXT,
    strong_code             VARCHAR(24),
    base_strong_code        VARCHAR(12),
    secondary_strong_codes  VARCHAR(24)[] NOT NULL DEFAULT '{}',
    lemma                   TEXT,
    lemma_normalized        TEXT,
    morph_code              VARCHAR(64),
    alignment_status        VARCHAR(32) NOT NULL -- exact_verse_match | unambiguous_form_match | untagged_f35_variant
);

CREATE INDEX IF NOT EXISTS idx_f35_tokens_ref ON f35_tokens (book_id, chapter, verse, word_position);
CREATE INDEX IF NOT EXISTS idx_f35_tokens_base_strong ON f35_tokens (base_strong_code);
CREATE INDEX IF NOT EXISTS idx_f35_tokens_strong ON f35_tokens (strong_code);
CREATE INDEX IF NOT EXISTS idx_f35_tokens_lemma_norm ON f35_tokens (lemma_normalized);
CREATE INDEX IF NOT EXISTS idx_f35_tokens_secondary_strongs ON f35_tokens USING GIN (secondary_strong_codes);

-- 7. ETL ingestion audit metadata
CREATE TABLE IF NOT EXISTS etl_metadata (
    source_name     VARCHAR(64) PRIMARY KEY,
    source_url      TEXT        NOT NULL,
    version_ref     VARCHAR(64),
    file_sha256     VARCHAR(64),
    record_count    INTEGER     NOT NULL,
    notes           TEXT,
    ingested_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
