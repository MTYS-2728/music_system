using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace TingGeRiZhi.Core;

/// <summary>
/// 数据导入导出：CSV / JSON 文本互转，以及 SQLite 在线备份与恢复。
/// 导入遇到坏数据只记警告并跳过，不抛异常；仅"文件不存在"会抛 <see cref="FileNotFoundException"/>。
/// </summary>
public sealed partial class LogDatabase
{
    // ---------------------------------------------------------------- 常量

    /// <summary>导出 CSV 的标准表头（九个字段）。</summary>
    private const string CsvHeaderLine = "记录时间,开始播放,歌曲名,歌手,专辑,时长秒,播放次数,是否收藏,备注";

    /// <summary>警告条数上限，避免超大文件产生海量提示。</summary>
    private const int MaxWarnings = 20;

    /// <summary>SQLite 数据库文件头（16 字节，含结尾的 0）。</summary>
    private static readonly byte[] SqliteHeaderBytes =
    {
        (byte)'S', (byte)'Q', (byte)'L', (byte)'i', (byte)'t', (byte)'e', (byte)' ',
        (byte)'f', (byte)'o', (byte)'r', (byte)'m', (byte)'a', (byte)'t', (byte)' ',
        (byte)'3', 0
    };

    /// <summary>带 BOM 的 UTF-8 编码（CSV / JSON 导出统一使用）。</summary>
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>JSON 导出选项：缩进 + 不转义中文。</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ---------------------------------------------------------------- CSV 导出

    /// <summary>
    /// 导出播放记录为 CSV：UTF-8 带 BOM、CRLF 换行、RFC 4180 引号转义。
    /// 时间按本地时间 <c>yyyy-MM-dd HH:mm:ss</c> 输出。
    /// </summary>
    public void ExportCsv(string path, IReadOnlyList<PlayLogRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        EnsureParentDirectory(path);

        using var writer = new StreamWriter(path, append: false, Utf8WithBom) { NewLine = "\r\n" };
        writer.WriteLine(CsvHeaderLine);
        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(",", new[]
            {
                FormatLocalTime(row.RecordedAtUtc),
                FormatLocalTime(row.StartedAtUtc),
                QuoteCsvField(row.Title),
                QuoteCsvField(row.Artist),
                QuoteCsvField(row.Album),
                row.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.SongPlayCount.ToString(CultureInfo.InvariantCulture),
                row.IsFavorite ? "是" : "否",
                QuoteCsvField(row.Notes)
            }));
        }
    }

    /// <summary>本地时间格式化，与旧版导出保持一致。</summary>
    private static string FormatLocalTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>RFC 4180 文本字段：整体加引号，内部引号翻倍。</summary>
    private static string QuoteCsvField(string? value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    // ---------------------------------------------------------------- JSON 导出

    /// <summary>
    /// 导出播放记录为 JSON：UTF-8 带 BOM、缩进、中文可读。
    /// 每行投影为扁平的固定结构（不直接序列化 <see cref="PlayLogRow"/>）。
    /// </summary>
    public void ExportJson(string path, IReadOnlyList<PlayLogRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        EnsureParentDirectory(path);

        var payload = rows.Select(row => new
        {
            recordedAt = ToIsoUtc(row.RecordedAtUtc),
            startedAt = ToIsoUtc(row.StartedAtUtc),
            title = row.Title,
            artist = row.Artist,
            album = row.Album,
            durationSeconds = row.DurationSeconds,
            playCount = row.SongPlayCount,
            isFavorite = row.IsFavorite,
            notes = row.Notes
        }).ToArray();

        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions), Utf8WithBom);
    }

    /// <summary>ISO-8601 往返格式的 UTC 字符串。</summary>
    private static string ToIsoUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- CSV 导入

    /// <summary>
    /// 导入 CSV。兼容三种来源：标准九列表头、旧版六列表头、英文表头；
    /// 若首行没有可识别的列名，则按旧版位置列（记录时间,开始播放,歌曲名,歌手,专辑,时长秒）处理第一行为数据。
    /// </summary>
    public ImportResult ImportCsv(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到要导入的 CSV 文件。", path);

        string text;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        List<List<string>> records;
        try
        {
            records = ParseCsv(text);
        }
        catch (Exception)
        {
            // 解析层理论上不抛异常；真出问题也只当整份内容跳过。
            return new ImportResult(0, 0, new[] { "CSV 内容无法解析，已全部跳过。" });
        }

        if (records.Count == 0) return ImportResult.Empty;

        // 表头识别：出现任一已知列名就按命名列处理，否则第一行当作旧版位置格式的数据行。
        var headerColumns = BuildColumnMap(records[0]);
        IReadOnlyDictionary<string, int> columns;
        int firstDataIndex;
        if (headerColumns.Count > 0)
        {
            columns = headerColumns;
            firstDataIndex = 1;
        }
        else
        {
            columns = LegacyPositionalColumns;
            firstDataIndex = 0;
        }

        var imported = 0;
        var skipped = 0;
        var warnings = new List<string>();

        for (var i = firstDataIndex; i < records.Count; i++)
        {
            var rowNumber = i + 1;
            var record = records[i];
            try
            {
                var accepted = ApplyImportedRow(
                    GetCsvField(record, columns, ColumnTitle),
                    GetCsvField(record, columns, ColumnArtist),
                    GetCsvField(record, columns, ColumnAlbum),
                    GetCsvField(record, columns, ColumnStartedAt),
                    GetCsvField(record, columns, ColumnRecordedAt),
                    GetCsvField(record, columns, ColumnDurationSeconds),
                    GetCsvField(record, columns, ColumnIsFavorite),
                    GetCsvField(record, columns, ColumnNotes),
                    rowNumber,
                    warnings);
                if (accepted) imported++; else skipped++;
            }
            catch (Exception)
            {
                skipped++;
                AddWarning(warnings, $"第 {rowNumber} 行：内容异常，已跳过。");
            }
        }

        return new ImportResult(imported, skipped, warnings);
    }

    /// <summary>旧版六列（无表头）的位置映射。</summary>
    private static readonly Dictionary<string, int> LegacyPositionalColumns = new(StringComparer.Ordinal)
    {
        [ColumnRecordedAt] = 0,
        [ColumnStartedAt] = 1,
        [ColumnTitle] = 2,
        [ColumnArtist] = 3,
        [ColumnAlbum] = 4,
        [ColumnDurationSeconds] = 5
    };

    /// <summary>把表头行归一化成 列名 -> 下标 的映射（同名列取第一次出现的位置）。</summary>
    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            var key = NormalizeColumnName(header[i]);
            if (key is not null && !map.ContainsKey(key)) map[key] = i;
        }
        return map;
    }

    private static string GetCsvField(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> columns, string key)
    {
        if (!columns.TryGetValue(key, out var index)) return "";
        return index >= 0 && index < record.Count ? record[index] : "";
    }

    // ---------------------------------------------------------------- JSON 导入

    /// <summary>
    /// 导入 JSON。兼容导出的 camelCase 结构，也兼容英文/中文键名的对象数组（大小写不敏感）。
    /// 若根节点是对象，则取其第一个数组属性（如 rows / records / data）作为记录集合。
    /// </summary>
    public ImportResult ImportJson(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到要导入的 JSON 文件。", path);

        string text;
        using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return new ImportResult(0, 0, new[] { "JSON 内容无法解析，已全部跳过。" });
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object) root = FindRecordArray(root);
            if (root.ValueKind != JsonValueKind.Array)
            {
                return new ImportResult(0, 0, new[] { "JSON 根节点不是记录数组，已全部跳过。" });
            }

            var imported = 0;
            var skipped = 0;
            var warnings = new List<string>();
            var rowNumber = 0;

            foreach (var element in root.EnumerateArray())
            {
                rowNumber++;
                if (element.ValueKind != JsonValueKind.Object)
                {
                    skipped++;
                    AddWarning(warnings, $"第 {rowNumber} 行：不是对象，已跳过。");
                    continue;
                }

                try
                {
                    var fields = BuildPropertyMap(element);
                    var accepted = ApplyImportedRow(
                        GetJsonText(fields, ColumnTitle),
                        GetJsonText(fields, ColumnArtist),
                        GetJsonText(fields, ColumnAlbum),
                        GetJsonText(fields, ColumnStartedAt),
                        GetJsonText(fields, ColumnRecordedAt),
                        GetJsonText(fields, ColumnDurationSeconds),
                        GetJsonText(fields, ColumnIsFavorite),
                        GetJsonText(fields, ColumnNotes),
                        rowNumber,
                        warnings);
                    if (accepted) imported++; else skipped++;
                }
                catch (Exception)
                {
                    skipped++;
                    AddWarning(warnings, $"第 {rowNumber} 行：内容异常，已跳过。");
                }
            }

            return new ImportResult(imported, skipped, warnings);
        }
    }

    /// <summary>在根对象里找第一个数组属性（优先常见的记录集合名）。</summary>
    private static JsonElement FindRecordArray(JsonElement root)
    {
        string[] preferred = { "rows", "records", "data", "items", "list", "记录" };
        foreach (var name in preferred)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array &&
                    string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }
            }
        }
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array) return property.Value;
        }
        return default;
    }

    /// <summary>把一条 JSON 对象的属性名归一化成 列名 -> 元素 的映射（大小写不敏感）。</summary>
    private static Dictionary<string, JsonElement> BuildPropertyMap(JsonElement element)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var key = NormalizeColumnName(property.Name);
            if (key is not null && !map.ContainsKey(key)) map[key] = property.Value;
        }
        return map;
    }

    private static string GetJsonText(IReadOnlyDictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var element) ? JsonScalarToString(element) : "";

    /// <summary>把任意 JSON 标量转成文本；对象/数组当作空值。</summary>
    private static string JsonScalarToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };

    // ---------------------------------------------------------------- 列名归一化

    private const string ColumnRecordedAt = "recordedat";
    private const string ColumnStartedAt = "startedat";
    private const string ColumnTitle = "title";
    private const string ColumnArtist = "artist";
    private const string ColumnAlbum = "album";
    private const string ColumnDurationSeconds = "durationseconds";
    private const string ColumnPlayCount = "playcount";
    private const string ColumnIsFavorite = "isfavorite";
    private const string ColumnNotes = "notes";

    /// <summary>中英文列名/属性名归一化（忽略大小写与首尾空白）；无法识别返回 null。</summary>
    private static string? NormalizeColumnName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return name.Trim().ToLowerInvariant() switch
        {
            "记录时间" or "记录时间utc" or "recordedat" or "recorded_at" or "recorded" => ColumnRecordedAt,
            "开始播放" or "播放时间" or "startedat" or "started_at" or "started" => ColumnStartedAt,
            "歌曲名" or "歌曲" or "歌名" or "标题" or "title" or "name" => ColumnTitle,
            "歌手" or "艺术家" or "artist" => ColumnArtist,
            "专辑" or "album" => ColumnAlbum,
            "时长秒" or "时长" or "durationseconds" or "duration_seconds" or "duration" or "seconds" => ColumnDurationSeconds,
            "播放次数" or "次数" or "playcount" or "play_count" => ColumnPlayCount,
            "是否收藏" or "收藏" or "isfavorite" or "is_favorite" or "favorite" => ColumnIsFavorite,
            "备注" or "注释" or "notes" or "note" => ColumnNotes,
            _ => null
        };
    }

    // ---------------------------------------------------------------- 导入公共逻辑

    /// <summary>
    /// 把一条已规范化的记录写入数据库。
    /// 歌曲名为空视为无效，返回 false（由调用方计为跳过）；其余字段缺失都按宽松规则处理。
    /// </summary>
    private bool ApplyImportedRow(
        string? title, string? artist, string? album,
        string? startedRaw, string? recordedRaw,
        string? durationRaw, string? favoriteRaw, string? notes,
        int rowNumber, List<string> warnings)
    {
        var cleanTitle = (title ?? "").Trim();
        if (cleanTitle.Length == 0)
        {
            AddWarning(warnings, $"第 {rowNumber} 行：缺少歌曲名，已跳过。");
            return false;
        }

        var startedAt = ResolveStartedAt(startedRaw, recordedRaw, rowNumber, warnings);
        var duration = ParseDuration(durationRaw);
        var songId = UpsertManualSong(cleanTitle, artist ?? "", album ?? "");
        AddRecord(songId, startedAt, duration, isManual: true);
        if (!string.IsNullOrEmpty(notes)) UpdateNotes(songId, notes);
        if (IsTruthy(favoriteRaw)) SetFavorite(songId, true);
        return true;
    }

    /// <summary>开始播放优先，其次记录时间；都不可用时用当前时间并记警告。</summary>
    private static DateTimeOffset ResolveStartedAt(string? startedRaw, string? recordedRaw, int rowNumber, List<string> warnings)
    {
        if (TryParseTime(startedRaw, out var started)) return started;
        if (TryParseTime(recordedRaw, out var recorded)) return recorded;
        AddWarning(warnings, $"第 {rowNumber} 行：播放时间缺失或无法解析，已使用当前时间。");
        return DateTimeOffset.Now;
    }

    private static bool TryParseTime(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
            out parsed);
    }

    private static int? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) return seconds;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var approx) && !double.IsNaN(approx))
        {
            return (int)Math.Round(approx);
        }
        return null;
    }

    /// <summary>收藏字段：是 / 1 / true / yes / y（大小写不敏感）视为真。</summary>
    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() switch
        {
            "是" or "1" or "true" or "yes" or "y" or "真" => true,
            _ => false
        };
    }

    /// <summary>警告只保留前 <see cref="MaxWarnings"/> 条。</summary>
    private static void AddWarning(List<string> warnings, string message)
    {
        if (warnings.Count < MaxWarnings) warnings.Add(message);
    }

    // ---------------------------------------------------------------- RFC 4180 解析

    /// <summary>
    /// 极简 RFC 4180 解析器：支持引号包裹字段、<c>""</c> 转义、字段内逗号 / 换行 / CRLF。
    /// 解析器自身不抛异常：孤立的引号按普通字符处理，未闭合的引号把剩余内容当作字段内容。
    /// </summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var index = 0;
        var length = text.Length;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldWasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            records.Add(new List<string>(fields));
            fields.Clear();
        }

        while (index < length)
        {
            var c = text[index];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (index + 1 < length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }
                    inQuotes = false;
                    index++;
                    continue;
                }
                field.Append(c);
                index++;
                continue;
            }

            switch (c)
            {
                case '"':
                    // 只有字段起始处的引号才是引用开始，其余位置按普通字符处理（宽松容错）。
                    if (field.Length == 0 && !fieldWasQuoted)
                    {
                        inQuotes = true;
                        fieldWasQuoted = true;
                    }
                    else
                    {
                        field.Append(c);
                    }
                    index++;
                    break;
                case ',':
                    EndField();
                    index++;
                    break;
                case '\r':
                    if (index + 1 < length && text[index + 1] == '\n') index++;
                    EndRecord();
                    index++;
                    break;
                case '\n':
                    EndRecord();
                    index++;
                    break;
                default:
                    field.Append(c);
                    index++;
                    break;
            }
        }

        // 文件末尾没有换行时补最后一条记录；纯空的收尾（正常换行结尾）不产生空行。
        if (field.Length > 0 || fields.Count > 0 || fieldWasQuoted) EndRecord();
        return records;
    }

    // ---------------------------------------------------------------- 备份 / 恢复

    /// <summary>
    /// 用 SQLite 在线备份接口导出一致性快照（应用运行中也能安全备份）。
    /// 目标目录不存在时自动创建，已存在的目标文件先删除。
    /// </summary>
    public void BackupTo(string path)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(DatabasePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("备份路径不能与当前数据库路径相同。", nameof(path));
        }

        EnsureParentDirectory(path);
        if (File.Exists(path)) File.Delete(path);

        using var source = Open();
        // 目标连接关闭连接池：否则 Sqlite 会把句柄留在池里，备份文件在进程结束前一直被占用。
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        destination.Open();
        source.BackupDatabase(destination);
    }

    /// <summary>
    /// 用备份文件覆盖当前数据库。
    /// 校验文件头必须是 SQLite 格式；覆盖前把现有数据库另存为 <c>*.before-restore</c>，
    /// 覆盖后调用 <see cref="Initialize"/>，让旧备份自动补齐当前版本的列与索引。
    /// </summary>
    public void RestoreFrom(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到要恢复的备份文件。", path);

        // 校验前 16 字节：SQLite format 3\0
        var header = new byte[16];
        var read = 0;
        using (var stream = File.OpenRead(path))
        {
            while (read < header.Length)
            {
                var chunk = stream.Read(header, read, header.Length - read);
                if (chunk <= 0) break;
                read += chunk;
            }
        }
        if (read < header.Length || !header.AsSpan().SequenceEqual(SqliteHeaderBytes))
        {
            throw new InvalidDataException("所选文件不是有效的 SQLite 数据库。");
        }

        // 先释放连接池句柄，保证文件可被复制。
        SqliteConnection.ClearAllPools();

        // 覆盖前留一份当前库的快照。
        if (File.Exists(DatabasePath))
        {
            File.Copy(DatabasePath, DatabasePath + ".before-restore", overwrite: true);
        }

        SqliteConnection.ClearAllPools();
        File.Copy(path, DatabasePath, overwrite: true);

        // 旧备份可能缺少新版本才有的列，重新初始化以补齐结构。
        Initialize();
    }

    // ---------------------------------------------------------------- 杂项工具

    /// <summary>确保目标文件的父目录存在。</summary>
    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }
}
