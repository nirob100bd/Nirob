using System.Data;
using System.Data.SQLite;
using System.Collections.Concurrent;

namespace Swifter.Core.Storage;

public sealed class DatabaseContext : IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(4, 16);
    private readonly ConcurrentBag<SQLiteConnection> _pool = new ConcurrentBag<SQLiteConnection>();
    private int _openConnections;
    public string Path { get; }
    public bool IsInitialized { get; private set; }

    public DatabaseContext(string path)
    {
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = path,
            JournalMode = SQLiteJournalModeEnum.Wal,
            SyncMode = SQLiteSynchronousEnum.Normal,
            Pooling = true,
            DefaultTimeout = 30,
            CacheSize = -8000,
            PageSize = 4096
        }.ToString();
    }

    public void Initialize()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaSql;
        cmd.ExecuteNonQuery();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=8000; PRAGMA cache_size=-8000; PRAGMA temp_store=MEMORY;";
        pragma.ExecuteNonQuery();
        IsInitialized = true;
    }

    public SQLiteConnection OpenConnection()
    {
        var conn = new SQLiteConnection(_connectionString);
        conn.Open();
        Interlocked.Increment(ref _openConnections);
        return conn;
    }

    public async Task<T> QueryAsync<T>(Func<SQLiteConnection, Task<T>> work)
    {
        var conn = OpenConnection();
        try
        {
            return await work(conn).ConfigureAwait(false);
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
        }
    }

    public T Query<T>(Func<SQLiteConnection, T> work)
    {
        var conn = OpenConnection();
        try
        {
            return work(conn);
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
        }
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] args)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        var conn = OpenConnection();
        try
        {
            await using var cmd = new SQLiteCommand(sql, conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
            _writeLock.Release();
        }
    }

    public int Execute(string sql, params (string Name, object Value)[] args)
    {
        _writeLock.Wait();
        var conn = OpenConnection();
        try
        {
            using var cmd = new SQLiteCommand(sql, conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteNonQuery();
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
            _writeLock.Release();
        }
    }

    public async Task<List<T>> QueryListAsync<T>(string sql, Func<SQLiteDataReader, Task<T>> map, params (string Name, object Value)[] args)
    {
        var conn = OpenConnection();
        try
        {
            await using var cmd = new SQLiteCommand(sql, conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            var results = new List<T>();
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.Default).ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                results.Add(await map(reader).ConfigureAwait(false));
            }
            return results;
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
        }
    }

    public List<T> QueryList<T>(string sql, Func<IDataReader, T> map, params (string Name, object Value)[] args)
    {
        var conn = OpenConnection();
        try
        {
            using var cmd = new SQLiteCommand(sql, conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            var results = new List<T>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) results.Add(map(reader));
            return results;
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
        }
    }

    public object Scalar(string sql, params (string Name, object Value)[] args)
    {
        var conn = OpenConnection();
        try
        {
            using var cmd = new SQLiteCommand(sql, conn);
            foreach (var (name, value) in args)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            return cmd.ExecuteScalar();
        }
        finally
        {
            conn.Dispose();
            Interlocked.Decrement(ref _openConnections);
        }
    }

    public int OpenConnectionCount => _openConnections;

    private const string SchemaSql = @"
CREATE TABLE IF NOT EXISTS history (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  url TEXT NOT NULL UNIQUE,
  title TEXT,
  visited_at INTEGER NOT NULL,
  visit_count INTEGER DEFAULT 1,
  typed_count INTEGER DEFAULT 0,
  transition TEXT,
  favicon BLOB,
  hidden INTEGER DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_history_url ON history(url);
CREATE INDEX IF NOT EXISTS idx_history_visited ON history(visited_at DESC);
CREATE INDEX IF NOT EXISTS idx_history_title ON history(title);

CREATE TABLE IF NOT EXISTS bookmarks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  parent_id INTEGER DEFAULT 0,
  title TEXT NOT NULL,
  url TEXT NOT NULL,
  favicon BLOB,
  created_at INTEGER NOT NULL,
  position INTEGER DEFAULT 0,
  is_folder INTEGER DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_bookmarks_parent ON bookmarks(parent_id);

CREATE TABLE IF NOT EXISTS cookies (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  host_key TEXT NOT NULL,
  name TEXT NOT NULL,
  value TEXT,
  encrypted_value BLOB,
  path TEXT NOT NULL DEFAULT '/',
  expires_utc INTEGER,
  is_secure INTEGER,
  is_http_only INTEGER,
  source_scheme INTEGER,
  created_at INTEGER,
  updated_at INTEGER,
  UNIQUE(host_key, name, path)
);
CREATE INDEX IF NOT EXISTS idx_cookies_host ON cookies(host_key);

CREATE TABLE IF NOT EXISTS sessions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_key TEXT NOT NULL UNIQUE,
  label TEXT,
  data_json TEXT NOT NULL,
  window_count INTEGER DEFAULT 0,
  tab_count INTEGER DEFAULT 0,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS passwords (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  origin TEXT NOT NULL,
  username TEXT NOT NULL,
  password_enc BLOB NOT NULL,
  password_iv BLOB NOT NULL,
  password_tag BLOB NOT NULL,
  note TEXT,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL,
  last_used INTEGER DEFAULT 0,
  UNIQUE(origin, username)
);

CREATE TABLE IF NOT EXISTS autofill (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  profile_name TEXT NOT NULL,
  field_name TEXT NOT NULL,
  field_value_enc BLOB NOT NULL,
  field_iv BLOB NOT NULL,
  field_tag BLOB NOT NULL,
  field_type TEXT,
  created_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS cache_entries (
  key TEXT PRIMARY KEY,
  value TEXT,
  blob BLOB,
  expires_at INTEGER,
  created_at INTEGER
);

CREATE TABLE IF NOT EXISTS downloads (
  id TEXT PRIMARY KEY,
  url TEXT,
  save_path TEXT,
  file_name TEXT,
  total_bytes INTEGER,
  received_bytes INTEGER,
  state INTEGER,
  segments_json TEXT,
  created_at INTEGER,
  completed_at INTEGER,
  is_media INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS themes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  json TEXT NOT NULL,
  is_active INTEGER DEFAULT 0,
  created_at INTEGER
);

CREATE TABLE IF NOT EXISTS extensions (
  id TEXT PRIMARY KEY,
  name TEXT,
  version TEXT,
  manifest_json TEXT,
  path TEXT,
  enabled INTEGER DEFAULT 1,
  installed_at INTEGER
);

CREATE TABLE IF NOT EXISTS hibernation (
  tab_id TEXT PRIMARY KEY,
  state_json TEXT,
  hibernated_at INTEGER,
  last_active INTEGER,
  saved_bytes INTEGER
);
";

    public void Dispose()
    {
        while (_pool.TryTake(out var conn))
        {
            try { conn.Dispose(); } catch { }
        }
        _writeLock?.Dispose();
        try { SQLiteConnection.ClearAllPools(); } catch { }
        IsInitialized = false;
    }
}
