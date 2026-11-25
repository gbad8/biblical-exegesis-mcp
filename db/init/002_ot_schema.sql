-- Biblical Exegesis MCP Server — Migration 002: Old Testament Masoretic Text (Hebrew & Aramaic)
-- Idempotent schema extension; safe to re-run against existing PostgreSQL volumes.

-- 1. Extend bible_books to cover all 66 books (OT 1..39 + NT 40..66)
ALTER TABLE bible_books
    ADD COLUMN IF NOT EXISTS testament VARCHAR(2) NOT NULL DEFAULT 'NT';

-- 2. Extend lexicon_entries to unify Greek (Gxxxx) and Hebrew/Aramaic (Hxxxx)
ALTER TABLE lexicon_entries
    ADD COLUMN IF NOT EXISTS language VARCHAR(8) NOT NULL DEFAULT 'grc';
ALTER TABLE lexicon_entries
    ADD COLUMN IF NOT EXISTS root_hebrew TEXT;
ALTER TABLE lexicon_entries
    ADD COLUMN IF NOT EXISTS twot_ref VARCHAR(32);
ALTER TABLE lexicon_entries
    ADD COLUMN IF NOT EXISTS occurrences_mt INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_lexicon_language ON lexicon_entries (language);
CREATE INDEX IF NOT EXISTS idx_lexicon_root_hebrew ON lexicon_entries (root_hebrew);

-- 3. Brown-Driver-Briggs Full Hebrew & English Lexicon (openscriptures/HebrewLexicon)
CREATE TABLE IF NOT EXISTS lexicon_bdb (
    id                  SERIAL PRIMARY KEY,
    bdb_id              VARCHAR(16) NOT NULL,
    strong_code         VARCHAR(24),
    base_strong_code    VARCHAR(12),
    headword            TEXT        NOT NULL,
    headword_normalized TEXT        NOT NULL,
    entry_type          VARCHAR(16),
    twot_ref            VARCHAR(32),
    summary_markdown    TEXT,
    full_markdown       TEXT        NOT NULL,
    char_length         INTEGER     NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_bdb_bdb_id ON lexicon_bdb (bdb_id);
CREATE INDEX IF NOT EXISTS idx_bdb_strong ON lexicon_bdb (strong_code);
CREATE INDEX IF NOT EXISTS idx_bdb_base_strong ON lexicon_bdb (base_strong_code);
CREATE INDEX IF NOT EXISTS idx_bdb_headword_norm ON lexicon_bdb (headword_normalized);

-- 4. Old Testament Masoretic Text tokens (STEPBible TAHOT — WLC/Leningrad, Qere/Ketiv, variants)
CREATE TABLE IF NOT EXISTS ot_tokens (
    id                      BIGSERIAL PRIMARY KEY,
    book_id                 SMALLINT      NOT NULL REFERENCES bible_books(book_id),
    chapter                 SMALLINT      NOT NULL,
    verse                   SMALLINT      NOT NULL,
    hebrew_ref              VARCHAR(32),
    word_position           SMALLINT      NOT NULL,
    sub_position            SMALLINT      NOT NULL DEFAULT 0,
    surface_form            TEXT          NOT NULL,
    segmented_form          TEXT          NOT NULL,
    surface_normalized      TEXT          NOT NULL,
    punctuation_after       TEXT,
    transliteration         TEXT,
    contextual_gloss        TEXT,
    strong_code             VARCHAR(24),
    base_strong_code        VARCHAR(12),
    prefix_suffix_strongs   VARCHAR(24)[] NOT NULL DEFAULT '{}',
    lemma                   TEXT,
    lemma_normalized        TEXT,
    morph_code              VARCHAR(64),
    language                VARCHAR(8)    NOT NULL DEFAULT 'heb',
    source_type             VARCHAR(16)   NOT NULL,
    in_mt                   BOOLEAN       NOT NULL DEFAULT TRUE,
    in_ketiv                BOOLEAN       NOT NULL DEFAULT FALSE,
    variant_note            TEXT
);

CREATE INDEX IF NOT EXISTS idx_ot_tokens_ref ON ot_tokens (book_id, chapter, verse, word_position, sub_position);
CREATE INDEX IF NOT EXISTS idx_ot_tokens_base_strong ON ot_tokens (base_strong_code);
CREATE INDEX IF NOT EXISTS idx_ot_tokens_strong ON ot_tokens (strong_code);
CREATE INDEX IF NOT EXISTS idx_ot_tokens_lemma_norm ON ot_tokens (lemma_normalized);
CREATE INDEX IF NOT EXISTS idx_ot_tokens_mt_strong ON ot_tokens (base_strong_code) WHERE in_mt = TRUE;
CREATE INDEX IF NOT EXISTS idx_ot_tokens_ketiv_strong ON ot_tokens (base_strong_code) WHERE in_ketiv = TRUE;
CREATE INDEX IF NOT EXISTS idx_ot_tokens_prefix_suffix ON ot_tokens USING GIN (prefix_suffix_strongs);
