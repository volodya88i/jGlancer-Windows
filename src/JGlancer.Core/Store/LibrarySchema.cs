namespace JGlancer.Core.Store;

/// <summary>
/// Versioned migrations of <c>library.sqlite</c>, identical to the macOS schema (docs/library-schema.sql). Never edit
/// a migration once it has shipped; add a new one.
/// </summary>
internal static class LibrarySchema
{
    public static IReadOnlyList<(string Id, string Sql)> Migrations { get; } =
    [
        ("v1-library", """
            CREATE TABLE root_folder (
              id INTEGER PRIMARY KEY,
              path TEXT NOT NULL UNIQUE,
              bookmark BLOB,
              volume_uuid TEXT,
              event_database_uuid TEXT,
              last_event_id INTEGER,
              is_recursive INTEGER NOT NULL DEFAULT 1,
              is_watched INTEGER NOT NULL DEFAULT 1,
              added_at INTEGER NOT NULL
            );
            CREATE TABLE folder (
              id INTEGER PRIMARY KEY,
              root_id INTEGER NOT NULL REFERENCES root_folder(id) ON DELETE CASCADE,
              parent_id INTEGER REFERENCES folder(id) ON DELETE CASCADE,
              rel_path TEXT NOT NULL,
              name TEXT NOT NULL,
              state INTEGER NOT NULL DEFAULT 0,
              UNIQUE(root_id, rel_path)
            );
            CREATE INDEX folder_parent ON folder(parent_id);
            CREATE TABLE asset (
              id INTEGER PRIMARY KEY,
              folder_id INTEGER NOT NULL REFERENCES folder(id) ON DELETE CASCADE,
              name TEXT NOT NULL,
              name_key TEXT NOT NULL,
              kind INTEGER NOT NULL,
              uti TEXT,
              size INTEGER NOT NULL,
              mtime INTEGER NOT NULL,
              birth_time INTEGER,
              inode INTEGER,
              quick_hash BLOB,
              width INTEGER,
              height INTEGER,
              orientation INTEGER,
              duration REAL,
              fps REAL,
              codec TEXT,
              captured_at INTEGER,
              rating INTEGER NOT NULL DEFAULT 0,
              flag INTEGER NOT NULL DEFAULT 0,
              state INTEGER NOT NULL DEFAULT 0,
              missing_since INTEGER,
              meta_state INTEGER NOT NULL DEFAULT 0,
              indexed_at INTEGER NOT NULL,
              UNIQUE(folder_id, name)
            );
            CREATE INDEX asset_folder_name_key ON asset(folder_id, name_key);
            CREATE INDEX asset_kind_mtime ON asset(kind, mtime);
            CREATE INDEX asset_captured ON asset(captured_at);
            CREATE INDEX asset_inode ON asset(inode);
            CREATE INDEX asset_pending_metadata ON asset(id) WHERE meta_state = 0 AND state = 0;
            CREATE INDEX asset_missing ON asset(size) WHERE state = 1;
            """),
        ("v2-albums", """
            CREATE TABLE album (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              created_at INTEGER NOT NULL,
              sort_order INTEGER NOT NULL DEFAULT 0,
              cover_asset_id INTEGER REFERENCES asset(id) ON DELETE SET NULL
            );
            CREATE TABLE album_item (
              album_id INTEGER NOT NULL REFERENCES album(id) ON DELETE CASCADE,
              asset_id INTEGER NOT NULL REFERENCES asset(id) ON DELETE CASCADE,
              position INTEGER NOT NULL,
              added_at INTEGER NOT NULL,
              PRIMARY KEY (album_id, asset_id)
            );
            CREATE INDEX album_item_order ON album_item(album_id, position);
            CREATE INDEX album_item_asset ON album_item(asset_id);
            """),
        // Substring search over file names (trigram FTS, case-insensitive also for Cyrillic, diacritics folded:
        // the bundled SQLite is newer than 3.45), kept in step with `asset` by triggers.
        ("v3-search", """
            CREATE VIRTUAL TABLE asset_search USING fts5(
              name, content='asset', content_rowid='id', tokenize='trigram remove_diacritics 1'
            );
            CREATE TRIGGER asset_search_insert AFTER INSERT ON asset BEGIN
              INSERT INTO asset_search(rowid, name) VALUES (new.id, new.name);
            END;
            CREATE TRIGGER asset_search_delete AFTER DELETE ON asset BEGIN
              INSERT INTO asset_search(asset_search, rowid, name) VALUES ('delete', old.id, old.name);
            END;
            CREATE TRIGGER asset_search_rename AFTER UPDATE OF name ON asset BEGIN
              INSERT INTO asset_search(asset_search, rowid, name) VALUES ('delete', old.id, old.name);
              INSERT INTO asset_search(rowid, name) VALUES (new.id, new.name);
            END;
            INSERT INTO asset_search(asset_search) VALUES ('rebuild');
            CREATE INDEX asset_flag ON asset(flag) WHERE flag != 0;
            """),
        // Tempo and key found in songs (W-107), by path: files outside the library count too.
        ("v4-audio-analysis", """
            CREATE TABLE audio_analysis (
              path TEXT PRIMARY KEY,
              size INTEGER NOT NULL,
              mtime INTEGER NOT NULL,
              bpm REAL,
              key_tonic INTEGER,
              key_minor INTEGER NOT NULL DEFAULT 0
            );
            """),
    ];
}
