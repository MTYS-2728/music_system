using System.Text.Json;
using TingGeRiZhi.Core;

// 探针：不打开主界面即可验证媒体会话读取、SQLite 读写、统计与导入导出。
//   dotnet run --project .\TingGeRiZhi.Probe\TingGeRiZhi.Probe.csproj -- --db-smoke
//   --stats-smoke / --io-smoke / --all

var failures = new List<string>();

if (args.Contains("--all")) args = new[] { "--db-smoke", "--stats-smoke", "--io-smoke", "--source-smoke" };

if (args.Contains("--sessions"))
{
    Console.WriteLine("== 当前所有 Windows 媒体会话 ==");
    var sessions = await new MediaSessionReader().ListSessionsAsync();
    if (sessions.Count == 0) Console.WriteLine("  （没有活动的媒体会话）");
    foreach (var s in sessions)
    {
        Console.WriteLine($"  {(s.IsCurrent ? "*" : " ")} [{(s.IsRecordable ? s.KindLabel : "其它")}] {s.FriendlySource}");
        Console.WriteLine($"      来源标识：{s.SourceApp}");
        Console.WriteLine($"      正在播放：{(string.IsNullOrWhiteSpace(s.Display) ? "(无标题)" : s.Display)}  状态={s.State}");
    }
    Console.WriteLine();
    var music = sessions.FirstOrDefault(s => s.IsRecordable);
    Console.WriteLine(music is null
        ? "  → 未找到音乐软件会话；开启「只记录音乐软件」时不会记录任何内容。"
        : $"  → 找到 {music.KindLabel} 会话：{(string.IsNullOrWhiteSpace(music.Display) ? "(无标题)" : music.Display)}");
    Console.WriteLine();
    if (args.Length == 1) return;
}

if (args.Contains("--source-smoke"))
{
    Console.WriteLine("== 来源识别冒烟 ==");

    // 可记录的播放器：进程名、完整路径、AUMID 三种形式都要认出来。
    Check(SourceApps.Match("QQMusic.exe")?.Name == "QQ 音乐", "QQMusic.exe 应识别为 QQ 音乐", failures);
    Check(SourceApps.Match(@"C:\Program Files\Tencent\QQMusic\QQMusic.exe")?.Name == "QQ 音乐",
        "QQ 音乐的完整路径应被识别", failures);
    Check(SourceApps.Match("cloudmusic.exe")?.Name == "网易云音乐", "cloudmusic.exe 应识别为网易云音乐", failures);
    Check(SourceApps.Match(@"C:\MySoftware\网易云音乐\cloudmusic.exe")?.Name == "网易云音乐",
        "网易云音乐的完整路径应被识别", failures);
    Check(SourceApps.Match("Netease.CloudMusic_abcdef!App")?.Name == "网易云音乐",
        "网易云音乐的 AUMID 应被识别", failures);
    Check(SourceApps.IsRecordable("  CLOUDMUSIC.EXE  "), "识别应忽略大小写与首尾空格", failures);

    // 不该被记录的来源。
    Check(!SourceApps.IsRecordable("msedge.exe"), "浏览器不应被识别为音乐软件", failures);
    Check(!SourceApps.IsRecordable("哔哩哔哩.exe"), "B 站客户端不应被识别为音乐软件", failures);
    Check(!SourceApps.IsRecordable(""), "空来源不应被识别", failures);
    Check(!SourceApps.IsRecordable(null), "null 来源不应被识别", failures);

    Console.WriteLine($"  可记录的播放器：{SourceApps.RecordableNames}");
}

if (args.Contains("--db-smoke"))
{
    Console.WriteLine("== SQLite 读写冒烟 ==");
    var path = Path.Combine(Path.GetTempPath(), "TingGeRiZhi", "smoke.db");
    if (File.Exists(path)) File.Delete(path);

    var db = new LogDatabase(path);
    var id = db.UpsertManualSong("测试歌曲", "测试歌手", "测试专辑");
    db.AddRecord(id, DateTimeOffset.UtcNow, 42);
    db.AddRecord(id, DateTimeOffset.UtcNow.AddMinutes(-5), 37);

    var sameId = db.UpsertManualSong("测试歌曲", "测试歌手", "测试专辑");
    Check(sameId == id, "同一首歌应复用同一个 song_id", failures);

    var rows = db.QueryRecords(new RecordQuery());
    Check(rows.Count == 2, $"应有 2 条播放记录，实际 {rows.Count}", failures);
    Check(rows.All(r => r.Title == "测试歌曲"), "标题应一致", failures);
    Check(rows.All(r => r.SongPlayCount == 2), "总播放次数应为 2", failures);

    db.SetFavorite(id, true);
    db.UpdateNotes(id, "一条备注");
    var song = db.GetSong(id);
    Check(song?.IsFavorite == true, "收藏标记应写回", failures);
    Check(song?.Notes == "一条备注", "备注应写回", failures);

    db.UpdateSongInfo(id, "改名的歌曲", "测试歌手", "测试专辑");
    var renamed = db.QueryRecords(new RecordQuery());
    Check(renamed.All(r => r.Title == "改名的歌曲"), "改名的歌曲应同步到记录", failures);

    db.DeleteRecords(new[] { rows[0].RecordId });
    Check(db.QueryRecords(new RecordQuery()).Count == 1, "删除一条记录后应剩 1 条", failures);
    Console.WriteLine($"  数据库：{path}");
    Console.WriteLine($"  记录：{db.QueryRecords(new RecordQuery()).Count} 条");
}

if (args.Contains("--stats-smoke"))
{
    Console.WriteLine("== 统计聚合冒烟 ==");
    var path = Path.Combine(Path.GetTempPath(), "TingGeRiZhi", "stats.db");
    if (File.Exists(path)) File.Delete(path);

    var db = new LogDatabase(path);
    var a = db.UpsertManualSong("甲", "歌手一", "专辑一");
    var b = db.UpsertManualSong("乙", "歌手二", "专辑一");
    db.AddRecord(a, DateTimeOffset.UtcNow, 200);
    db.AddRecord(a, DateTimeOffset.UtcNow.AddDays(-1), 180);
    db.AddRecord(b, DateTimeOffset.UtcNow.AddHours(-2), null);
    db.SetFavorite(a, true);

    var overview = db.GetOverview();
    Check(overview.Kpi.TotalPlays == 3, $"总播放应为 3，实际 {overview.Kpi.TotalPlays}", failures);
    Check(overview.Kpi.TotalSongs == 2, $"歌曲数应为 2，实际 {overview.Kpi.TotalSongs}", failures);
    Check(overview.Kpi.TotalArtists == 2, $"歌手数应为 2，实际 {overview.Kpi.TotalArtists}", failures);
    Check(overview.Kpi.FavoriteSongs == 1, "收藏数应为 1", failures);
    Check(overview.Kpi.TotalSeconds == 380, $"累计时长应为 380，实际 {overview.Kpi.TotalSeconds}", failures);
    Check(overview.HourDistribution.Count == 24, "时段分布应恒为 24 项", failures);
    Check(overview.WeekdayDistribution.Count == 7, "星期分布应恒为 7 项", failures);
    Check(overview.DailyTrend.Count == 30, "趋势应恒为 30 项", failures);
    Check(overview.TopSongs.FirstOrDefault()?.Label == "甲", "最常播放的应是《甲》", failures);
    Console.WriteLine($"  KPI：{overview.Kpi.TotalPlays} 次 / {overview.Kpi.TotalSongs} 首 / 时长 {overview.Kpi.TotalSeconds}s");
}

if (args.Contains("--io-smoke"))
{
    Console.WriteLine("== 导入导出冒烟 ==");
    var root = Path.Combine(Path.GetTempPath(), "TingGeRiZhi", "io");
    Directory.CreateDirectory(root);
    var source = Path.Combine(root, "source.db");
    if (File.Exists(source)) File.Delete(source);

    var db = new LogDatabase(source);
    var id = db.UpsertManualSong("带,逗号\"引号", "歌手", "专辑");
    db.AddRecord(id, DateTimeOffset.UtcNow, 123);
    db.UpdateNotes(id, "第一行\r\n第二行");
    db.SetFavorite(id, true);

    var rows = db.QueryRecords(new RecordQuery());
    var csv = Path.Combine(root, "out.csv");
    var json = Path.Combine(root, "out.json");
    var backup = Path.Combine(root, "backup.db");
    db.ExportCsv(csv, rows);
    db.ExportJson(json, rows);
    db.BackupTo(backup);

    var target = Path.Combine(root, "target.db");
    if (File.Exists(target)) File.Delete(target);
    var restored = new LogDatabase(target);
    var csvResult = restored.ImportCsv(csv);
    Check(csvResult.Imported == 1, $"CSV 应导入 1 条，实际 {csvResult.Imported}", failures);
    var imported = restored.QueryRecords(new RecordQuery()).Single();
    Check(imported.Title == "带,逗号\"引号", "CSV 往返后标题应一致", failures);
    Check(imported.DurationSeconds == 123, "CSV 往返后时长应一致", failures);
    Check(imported.IsFavorite, "CSV 往返后收藏应一致", failures);

    var jsonTarget = Path.Combine(root, "target2.db");
    if (File.Exists(jsonTarget)) File.Delete(jsonTarget);
    var fromJson = new LogDatabase(jsonTarget);
    var jsonResult = fromJson.ImportJson(json);
    Check(jsonResult.Imported == 1, $"JSON 应导入 1 条，实际 {jsonResult.Imported}", failures);
    Check(File.Exists(backup) && new FileInfo(backup).Length > 0, "备份文件应存在且非空", failures);
    Console.WriteLine($"  导出：{csv} / {json} / 备份 {backup}");
}

if (failures.Count > 0)
{
    Console.WriteLine();
    foreach (var failure in failures) Console.WriteLine($"  [失败] {failure}");
    Console.WriteLine($"共 {failures.Count} 项断言失败。");
    Environment.ExitCode = 1;
    return;
}

if (args.Length > 0)
{
    Console.WriteLine();
    Console.WriteLine("全部断言通过。");
    return;
}

// ---------------------------------------------------------------- 媒体会话探针
Console.WriteLine("聆迹 · Windows 媒体会话探针");
Console.WriteLine("正在读取当前媒体会话…");
var reader = new MediaSessionReader();
var snapshot = await reader.ReadCurrentAsync();
Console.WriteLine(JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));

if (snapshot.IsAvailable)
{
    var bytes = await reader.ReadThumbnailAsync();
    Console.WriteLine($"封面缩略图：{(bytes is null ? "会话未提供" : $"{bytes.Length} 字节")}");
    Console.WriteLine();
    Console.WriteLine("读取成功：请用播放器切歌 / 暂停后再次运行本探针。");
}
else
{
    Console.WriteLine();
    Console.WriteLine($"读取失败或没有活动会话：{snapshot.Error}");
}

static void Check(bool condition, string message, List<string> failures)
{
    Console.WriteLine($"  [{(condition ? "ok" : "!!")}] {message}");
    if (!condition) failures.Add(message);
}
