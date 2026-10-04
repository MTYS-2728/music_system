using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TingGeRiZhi.Core;

/// <summary>
/// 本地 SQLite 听歌日志数据库。
/// 统计查询见 <c>LogDatabase.Stats.cs</c>，导入导出见 <c>LogDatabase.Transfer.cs</c>。
/// </summary>
public sealed partial class LogDatabase
{
    public string DatabasePath { get; }
    private readonly string _connectionString;

    /// <summary>当前数据库的目录。</summary>
    public string DatabaseDirectory => Path.GetDirectoryName(DatabasePath) ?? ".";

    public LogDatabase(string? databasePath = null)
    {
        var preferred = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TingGeRiZhi", "tingge.db");
        DatabasePath = preferred;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
            EnsureWritable(Path.GetDirectoryName(DatabasePath)!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            DatabasePath = Path.Combine(Path.GetTempPath(), "TingGeRiZhi", "tingge.db");
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            ForeignKeys = true
        }.ToString();
        Initialize();
    }

    /// <summary>用一次真实写入探测目录权限，避免只在插入数据时才失败。</summary>
    private static void EnsureWritable(string directory)
    {
        var probe = Path.Combine(directory, ".write-probe");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Initialize()
    {
        using var connection = Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS songs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT NOT NULL,
  artist TEXT NOT NULL DEFAULT '',
  album TEXT NOT NULL DEFAULT '',
  cover_path TEXT NULL,
  source TEXT NOT NULL DEFAULT '',
  original_link TEXT NULL,
  is_favorite INTEGER NOT NULL DEFAULT 0,
  notes TEXT NOT NULL DEFAULT '',
  created_at_utc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ix_songs_identity ON songs(title, artist, album, source);
CREATE TABLE IF NOT EXISTS play_records (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  song_id INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE,
  started_at_utc TEXT NOT NULL,
  recorded_at_utc TEXT NOT NULL,
  duration_seconds INTEGER NULL,
  source TEXT NOT NULL DEFAULT '',
  is_manual INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_play_records_started ON play_records(started_at_utc);
CREATE INDEX IF NOT EXISTS ix_play_records_song ON play_records(song_id);
";
            command.ExecuteNonQuery();
        }

        // 兼容旧库：补齐可能缺失的列（不删除任何历史列）。
        EnsureColumn(connection, "songs", "cover_path", "TEXT NULL");
        EnsureColumn(connection, "songs", "is_favorite", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "songs", "notes", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "songs", "source", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "play_records", "duration_seconds", "INTEGER NULL");
        EnsureColumn(connection, "play_records", "is_manual", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "play_records", "source", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using (var check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
            }
        }
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    internal static string ToDb(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    internal static DateTimeOffset FromDb(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    // ---------------------------------------------------------------- 歌曲

    /// <summary>按 标题/歌手/专辑 查找或新建歌曲，返回 song_id。</summary>
    public long UpsertSong(MediaSnapshot snapshot, string? coverPath = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        long id;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT id FROM songs WHERE title=$title AND artist=$artist AND album=$album LIMIT 1";
            find.Parameters.AddWithValue("$title", snapshot.Title);
            find.Parameters.AddWithValue("$artist", snapshot.Artist);
            find.Parameters.AddWithValue("$album", snapshot.Album);
            var existing = find.ExecuteScalar();
            if (existing is long found)
            {
                id = found;
            }
            else
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"INSERT INTO songs(title,artist,album,cover_path,source,created_at_utc)
VALUES($title,$artist,$album,$cover,'',$created); SELECT last_insert_rowid();";
                insert.Parameters.AddWithValue("$title", snapshot.Title);
                insert.Parameters.AddWithValue("$artist", snapshot.Artist);
                insert.Parameters.AddWithValue("$album", snapshot.Album);
                insert.Parameters.AddWithValue("$cover", (object?)coverPath ?? DBNull.Value);
                insert.Parameters.AddWithValue("$created", ToDb(DateTimeOffset.UtcNow));
                id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }

        // 抓到封面后补写，但不覆盖用户手工选择的封面。
        if (!string.IsNullOrWhiteSpace(coverPath))
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE songs SET cover_path=$cover WHERE id=$id AND (cover_path IS NULL OR cover_path='')";
            update.Parameters.AddWithValue("$cover", coverPath);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return id;
    }

    public long UpsertManualSong(string title, string artist, string album)
    {
        var snapshot = new MediaSnapshot(title.Trim(), artist.Trim(), album.Trim(), PlaybackState.Unknown,
            null, null, "手动", DateTimeOffset.UtcNow, true);
        return UpsertSong(snapshot);
    }

    /// <summary>修改歌曲资料；若与已有歌曲重复则合并播放记录。</summary>
    public void UpdateSongInfo(long songId, string title, string artist, string album)
    {
        title = title.Trim();
        artist = artist.Trim();
        album = album.Trim();
        if (title.Length == 0) throw new ArgumentException("歌曲名不能为空。", nameof(title));

        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        long targetId = songId;
        using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = "SELECT id FROM songs WHERE title=$title AND artist=$artist AND album=$album AND id<>$id LIMIT 1";
            find.Parameters.AddWithValue("$title", title);
            find.Parameters.AddWithValue("$artist", artist);
            find.Parameters.AddWithValue("$album", album);
            find.Parameters.AddWithValue("$id", songId);
            if (find.ExecuteScalar() is long duplicate) targetId = duplicate;
        }

        if (targetId == songId)
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE songs SET title=$title, artist=$artist, album=$album WHERE id=$id";
            update.Parameters.AddWithValue("$title", title);
            update.Parameters.AddWithValue("$artist", artist);
            update.Parameters.AddWithValue("$album", album);
            update.Parameters.AddWithValue("$id", songId);
            update.ExecuteNonQuery();
        }
        else
        {
            // 合并到已存在的歌曲：转移播放记录、保留非空备注/收藏/封面，再删掉原行。
            using (var move = connection.CreateCommand())
            {
                move.Transaction = transaction;
                move.CommandText = "UPDATE play_records SET song_id=$target WHERE song_id=$source";
                move.Parameters.AddWithValue("$target", targetId);
                move.Parameters.AddWithValue("$source", songId);
                move.ExecuteNonQuery();
            }
            using (var merge = connection.CreateCommand())
            {
                merge.Transaction = transaction;
                merge.CommandText = @"UPDATE songs SET
  is_favorite = MAX(is_favorite, (SELECT is_favorite FROM songs WHERE id=$source)),
  notes = CASE WHEN COALESCE(notes,'')='' THEN (SELECT notes FROM songs WHERE id=$source) ELSE notes END,
  cover_path = COALESCE(NULLIF(cover_path,''), (SELECT cover_path FROM songs WHERE id=$source))
WHERE id=$target";
                merge.Parameters.AddWithValue("$target", targetId);
                merge.Parameters.AddWithValue("$source", songId);
                merge.ExecuteNonQuery();
            }
            using (var drop = connection.CreateCommand())
            {
                drop.Transaction = transaction;
                drop.CommandText = "DELETE FROM songs WHERE id=$source";
                drop.Parameters.AddWithValue("$source", songId);
                drop.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public void UpdateNotes(long songId, string notes)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE songs SET notes=$notes WHERE id=$id";
        command.Parameters.AddWithValue("$id", songId);
        command.Parameters.AddWithValue("$notes", notes ?? "");
        command.ExecuteNonQuery();
    }

    public void SetFavorite(long songId, bool favorite)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE songs SET is_favorite=$fav WHERE id=$id";
        command.Parameters.AddWithValue("$fav", favorite ? 1 : 0);
        command.Parameters.AddWithValue("$id", songId);
        command.ExecuteNonQuery();
    }

    public void SetCoverPath(long songId, string? coverPath)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE songs SET cover_path=$cover WHERE id=$id";
        command.Parameters.AddWithValue("$cover", (object?)coverPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", songId);
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- 播放记录

    public void AddRecord(long songId, DateTimeOffset startedAtUtc, int? durationSeconds, bool isManual = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"INSERT INTO play_records(song_id,started_at_utc,recorded_at_utc,duration_seconds,source,is_manual)
VALUES($song,$started,$recorded,$duration,'',$manual)";
        command.Parameters.AddWithValue("$song", songId);
        command.Parameters.AddWithValue("$started", ToDb(startedAtUtc));
        command.Parameters.AddWithValue("$recorded", ToDb(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$duration", (object?)durationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$manual", isManual ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public void DeleteRecords(IReadOnlyCollection<long> recordIds)
    {
        if (recordIds.Count == 0) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var placeholders = recordIds.Select((_, index) => $"$id{index}").ToArray();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM play_records WHERE id IN ({string.Join(',', placeholders)})";
        var index = 0;
        foreach (var id in recordIds) command.Parameters.AddWithValue(placeholders[index++], id);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>删除歌曲本身及其全部播放记录。</summary>
    public void DeleteSongs(IReadOnlyCollection<long> songIds)
    {
        if (songIds.Count == 0) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var placeholders = songIds.Select((_, index) => $"$id{index}").ToArray();
        using (var records = connection.CreateCommand())
        {
            records.Transaction = transaction;
            records.CommandText = $"DELETE FROM play_records WHERE song_id IN ({string.Join(',', placeholders)})";
            var index = 0;
            foreach (var id in songIds) records.Parameters.AddWithValue(placeholders[index++], id);
            records.ExecuteNonQuery();
        }
        using (var songs = connection.CreateCommand())
        {
            songs.Transaction = transaction;
            songs.CommandText = $"DELETE FROM songs WHERE id IN ({string.Join(',', placeholders)})";
            var index = 0;
            foreach (var id in songIds) songs.Parameters.AddWithValue(placeholders[index++], id);
            songs.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>清空全部播放记录；<paramref name="alsoSongs"/> 为真时同时删除歌曲资料与备注。</summary>
    public int DeleteAll(bool alsoSongs)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        int removed;
        using (var count = connection.CreateCommand())
        {
            count.Transaction = transaction;
            count.CommandText = "SELECT COUNT(*) FROM play_records";
            removed = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        using (var records = connection.CreateCommand())
        {
            records.Transaction = transaction;
            records.CommandText = "DELETE FROM play_records";
            records.ExecuteNonQuery();
        }
        if (alsoSongs)
        {
            using var songs = connection.CreateCommand();
            songs.Transaction = transaction;
            songs.CommandText = "DELETE FROM songs";
            songs.ExecuteNonQuery();
        }
        transaction.Commit();
        return removed;
    }

    private static string SortClause(RecordSort sort) => sort switch
    {
        RecordSort.StartedAscending => "ORDER BY r.started_at_utc ASC, r.id ASC",
        RecordSort.Title => "ORDER BY s.title COLLATE NOCASE ASC, r.started_at_utc DESC",
        RecordSort.Artist => "ORDER BY s.artist COLLATE NOCASE ASC, r.started_at_utc DESC",
        RecordSort.PlayCount => "ORDER BY play_count DESC, r.started_at_utc DESC",
        RecordSort.Duration => "ORDER BY r.duration_seconds DESC, r.started_at_utc DESC",
        _ => "ORDER BY r.started_at_utc DESC, r.id DESC"
    };

    private static void BindFilters(SqliteCommand command, RecordQuery query)
    {
        command.Parameters.AddWithValue("$from", (object?)query.FromUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$to", (object?)query.ToUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? DBNull.Value);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        command.Parameters.AddWithValue("$search", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(search)}%");
        command.Parameters.AddWithValue("$artist", (object?)query.Artist ?? DBNull.Value);
        command.Parameters.AddWithValue("$album", (object?)query.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$fav", query.FavoritesOnly ? 1 : 0);
    }

    private static string EscapeLike(string? value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private const string FilterSql = @"
WHERE ($from IS NULL OR r.started_at_utc >= $from)
  AND ($to IS NULL OR r.started_at_utc < $to)
  AND ($search IS NULL OR s.title LIKE $like ESCAPE '\' OR s.artist LIKE $like ESCAPE '\' OR s.album LIKE $like ESCAPE '\')
  AND ($artist IS NULL OR s.artist = $artist)
  AND ($album IS NULL OR s.album = $album)
  AND ($fav = 0 OR s.is_favorite = 1)";

    public IReadOnlyList<PlayLogRow> QueryRecords(RecordQuery query)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT r.id, r.song_id, s.title, s.artist, s.album, r.started_at_utc, r.recorded_at_utc,
       r.duration_seconds, r.is_manual, COALESCE(s.notes,''), s.is_favorite, s.cover_path,
       (SELECT COUNT(*) FROM play_records pr WHERE pr.song_id = r.song_id) AS play_count
FROM play_records r JOIN songs s ON s.id = r.song_id
{FilterSql}
{SortClause(query.Sort)}";
        BindFilters(command, query);
        using var reader = command.ExecuteReader();
        var rows = new List<PlayLogRow>();
        while (reader.Read()) rows.Add(ReadLogRow(reader));
        return rows;
    }

    private static PlayLogRow ReadLogRow(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        FromDb(reader.GetString(5)),
        FromDb(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetInt32(7),
        reader.GetInt32(8) == 1,
        reader.GetString(9),
        reader.GetInt32(10) == 1,
        reader.IsDBNull(11) ? null : reader.GetString(11),
        reader.GetInt32(12));

    /// <summary>查询歌曲库（按歌曲去重）。</summary>
    public IReadOnlyList<SongRow> QuerySongs(RecordQuery query, int limit = 0, int offset = 0)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT s.id, s.title, s.artist, s.album, s.cover_path, COALESCE(s.notes,''), s.is_favorite,
       COUNT(r.id) AS play_count,
       COALESCE(SUM(r.duration_seconds), 0) AS total_seconds,
       MAX(r.started_at_utc) AS last_played
FROM songs s LEFT JOIN play_records r ON r.song_id = s.id
WHERE ($search IS NULL OR s.title LIKE $like ESCAPE '\' OR s.artist LIKE $like ESCAPE '\' OR s.album LIKE $like ESCAPE '\')
  AND ($artist IS NULL OR s.artist = $artist)
  AND ($album IS NULL OR s.album = $album)
  AND ($fav = 0 OR s.is_favorite = 1)
GROUP BY s.id
ORDER BY last_played DESC NULLS LAST, s.title COLLATE NOCASE ASC
{(limit > 0 ? "LIMIT $limit OFFSET $offset" : "")}";
        command.Parameters.AddWithValue("$search", (object?)(string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim()) ?? DBNull.Value);
        command.Parameters.AddWithValue("$like", $"%{EscapeLike(query.Search?.Trim())}%");
        command.Parameters.AddWithValue("$artist", (object?)query.Artist ?? DBNull.Value);
        command.Parameters.AddWithValue("$album", (object?)query.Album ?? DBNull.Value);
        command.Parameters.AddWithValue("$fav", query.FavoritesOnly ? 1 : 0);
        if (limit > 0)
        {
            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<SongRow>();
        while (reader.Read())
        {
            rows.Add(new SongRow(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetInt32(6) == 1,
                reader.GetInt32(7), reader.GetInt32(8),
                reader.IsDBNull(9) ? null : FromDb(reader.GetString(9))));
        }
        return rows;
    }

    /// <summary>按 标题/歌手/专辑 查找已存在的歌曲 id（不创建）。</summary>
    public long? FindSongId(string title, string artist, string album)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM songs WHERE title=$title AND artist=$artist AND album=$album LIMIT 1";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$artist", artist);
        command.Parameters.AddWithValue("$album", album);
        var result = command.ExecuteScalar();
        return result is long id ? id : null;
    }

    public Song? GetSong(long songId)    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title,artist,album,cover_path,COALESCE(notes,''),is_favorite,created_at_utc FROM songs WHERE id=$id";
        command.Parameters.AddWithValue("$id", songId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Song
        {
            Id = reader.GetInt64(0),
            Title = reader.GetString(1),
            Artist = reader.GetString(2),
            Album = reader.GetString(3),
            CoverPath = reader.IsDBNull(4) ? null : reader.GetString(4),
            Notes = reader.GetString(5),
            IsFavorite = reader.GetInt32(6) == 1,
            CreatedAtUtc = FromDb(reader.GetString(7))
        };
    }

    /// <summary>数据库是否还没有任何歌曲。</summary>
    public bool IsEmpty()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM songs)";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0;
    }
}
