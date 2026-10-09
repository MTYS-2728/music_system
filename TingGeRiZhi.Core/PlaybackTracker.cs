namespace TingGeRiZhi.Core;

/// <summary>
/// 播放跟踪器：定时轮询媒体会话，累计连续播放时长，达到阈值后自动写入一条播放记录。
/// 同一首歌的单曲循环会按"重新开始"识别为一次新的播放。
/// </summary>
public sealed class PlaybackTracker : IDisposable
{
    private readonly MediaSessionReader _reader;
    private readonly LogDatabase _database;
    private readonly CoverCache? _coverCache;
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly object _gate = new();

    private AppSettings _settings;
    private Timer? _timer;
    private string? _currentKey;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastSeen;
    private double _continuousSeconds;
    private bool _recorded;
    private int _unavailableTicks;
    private bool _wasPaused;
    private TimeSpan? _lastPosition;
    private bool _coverAttempted;
    private string? _coverPath;

    /// <summary>自动记录开关（运行时可切换）。</summary>
    public bool IsAutoRecording { get; private set; }

    /// <summary>最近一次读取到的媒体会话（已附带封面路径）。</summary>
    public MediaSnapshot? LastSnapshot { get; private set; }

    public double ContinuousSeconds { get { lock (_gate) return _continuousSeconds; } }

    /// <summary>距离自动记录还差多少秒。</summary>
    public double SecondsUntilRecord
    {
        get { lock (_gate) return Math.Max(0, _settings.RecordThresholdSeconds - _continuousSeconds); }
    }

    public string? CurrentCoverPath { get { lock (_gate) return _coverPath; } }

    public event EventHandler<MediaSnapshot>? SnapshotUpdated;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<PlayRecord>? RecordCreated;
    public event EventHandler<bool>? AutoRecordingChanged;

    public PlaybackTracker(MediaSessionReader reader, LogDatabase database, AppSettings settings, CoverCache? coverCache = null)
    {
        _reader = reader;
        _database = database;
        _coverCache = coverCache;
        _settings = settings;
        IsAutoRecording = settings.AutoRecord;
    }

    public void Start()
    {
        var interval = TimeSpan.FromSeconds(_settings.PollSeconds);
        _timer = new Timer(_ => _ = SafeTickAsync(), null, TimeSpan.Zero, interval);
    }

    private async Task SafeTickAsync()
    {
        try { await TickAsync().ConfigureAwait(false); }
        catch (Exception) { /* 后台轮询绝不抛出，避免进程崩溃 */ }
    }

    /// <summary>应用新的设置（轮询间隔变化时立即生效）。</summary>
    public void ApplySettings(AppSettings settings)
    {
        bool intervalChanged;
        lock (_gate)
        {
            intervalChanged = _settings.PollSeconds != settings.PollSeconds;
            _settings = settings;
        }
        if (intervalChanged) _timer?.Change(TimeSpan.Zero, TimeSpan.FromSeconds(settings.PollSeconds));
    }

    public void SetAutoRecording(bool enabled)
    {
        IsAutoRecording = enabled;
        AutoRecordingChanged?.Invoke(this, enabled);
        StatusChanged?.Invoke(this, enabled ? "自动记录已开启" : "自动记录已暂停");
    }

    /// <summary>读取一次媒体会话并推进状态机；重入时直接返回上一次快照。</summary>
    public async Task<MediaSnapshot> TickAsync(CancellationToken cancellationToken = default)
    {
        if (!await _tickGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return LastSnapshot ?? new MediaSnapshot("", "", "", PlaybackState.Unknown, null, null, "",
                DateTimeOffset.UtcNow, false, "读取进行中");
        }

        try
        {
            var raw = await ReadSnapshotAsync(cancellationToken).ConfigureAwait(false);

            if (!raw.IsAvailable)
            {
                lock (_gate)
                {
                    _unavailableTicks++;
                    if (_unavailableTicks >= 2) ResetSessionLocked();
                }
                LastSnapshot = raw;
                SnapshotUpdated?.Invoke(this, raw);
                StatusChanged?.Invoke(this, raw.Error ?? "无法读取当前播放信息");
                return raw;
            }

            bool startedNewSession;
            lock (_gate) startedNewSession = UpdateSessionLocked(raw);

            if (startedNewSession) await TryFetchCoverAsync(raw, cancellationToken).ConfigureAwait(false);

            MediaSnapshot result;
            PlayRecord? created;
            string status;
            lock (_gate)
            {
                result = raw with { CoverPath = _coverPath };
                LastSnapshot = result;
                AccumulateLocked(result);
                created = MaybeRecordLocked(result, out status);
            }

            SnapshotUpdated?.Invoke(this, result);
            if (created is not null) RecordCreated?.Invoke(this, created);
            StatusChanged?.Invoke(this, status);
            return result;
        }
        finally
        {
            _tickGate.Release();
        }
    }

    /// <summary>手动记录当前会话（不检查阈值）。</summary>
    public bool TryManualRecord(out string message)
    {
        lock (_gate)
        {
            if (LastSnapshot is not { IsAvailable: true } snapshot)
            {
                message = "当前没有可记录的媒体会话";
                return false;
            }
            var id = _database.UpsertSong(snapshot, _coverPath);
            var duration = ResolveDuration(snapshot);
            _database.AddRecord(id, snapshot.ReadAtUtc, duration, isManual: true);
            message = $"已手动记录：{snapshot.Title} - {snapshot.Artist}";
            StatusChanged?.Invoke(this, message);
            return true;
        }
    }

    /// <summary>记录一首手动填写的歌曲。</summary>
    public void RecordManualSong(string title, string artist, string album, DateTimeOffset? startedAtUtc = null)
    {
        var id = _database.UpsertManualSong(title, artist, album);
        _database.AddRecord(id, startedAtUtc ?? DateTimeOffset.UtcNow, null, isManual: true);
    }

    // ------------------------------------------------------------------ 内部

    /// <summary>
    /// 按设置挑选要跟踪的媒体会话。开启"只记录音乐软件"时，其它应用的会话一律跳过，
    /// 并返回一条带说明的不可用快照，让界面能告诉用户"为什么没有记录"。
    /// </summary>
    private async Task<MediaSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!_settings.OnlyRecordMusicApps)
            return await _reader.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);

        var matched = await _reader.ReadPreferredAsync(SourceApps.IsRecordable, cancellationToken).ConfigureAwait(false);
        if (matched is not null) return matched;

        var sessions = await _reader.ListSessionsAsync(cancellationToken).ConfigureAwait(false);
        var other = sessions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Title));

        if (other is null)
        {
            return new MediaSnapshot("", "", "", PlaybackState.Closed, null, null, "",
                DateTimeOffset.UtcNow, false, "没有活动的 Windows 媒体会话");
        }

        return new MediaSnapshot(other.Title, other.Artist, "", PlaybackState.Closed, null, null, other.SourceApp,
            DateTimeOffset.UtcNow, false, $"「{other.FriendlySource}」正在播放，已按设置跳过（仅记录 {SourceApps.RecordableNames}）");
    }

    private bool UpdateSessionLocked(MediaSnapshot raw)
    {
        var now = raw.ReadAtUtc;
        var keyChanged = !string.Equals(_currentKey, raw.StableKey, StringComparison.Ordinal);
        var loopRestart = !keyChanged && IsLoopRestartLocked(raw);

        if (keyChanged || loopRestart)
        {
            _currentKey = raw.StableKey;
            _startedAt = now;
            _lastSeen = now;
            _continuousSeconds = 0;
            _recorded = false;
            _coverAttempted = false;
            _coverPath = null;
            _lastPosition = raw.Position;
            return true;
        }

        _lastPosition = raw.Position;
        return false;
    }

    /// <summary>单曲循环时进度会跳回开头，这里据此判定为"新的一次播放"。</summary>
    private bool IsLoopRestartLocked(MediaSnapshot raw)
    {
        if (raw.State != PlaybackState.Playing) return false;
        if (raw.Position is not { } position || _lastPosition is not { } previous) return false;
        return position < TimeSpan.FromSeconds(3) && previous > TimeSpan.FromSeconds(10);
    }

    private void AccumulateLocked(MediaSnapshot snapshot)
    {
        var now = snapshot.ReadAtUtc;
        if (snapshot.State == PlaybackState.Playing && !_wasPaused)
        {
            _continuousSeconds += Math.Max(0, (now - _lastSeen).TotalSeconds);
        }
        _wasPaused = snapshot.State == PlaybackState.Paused;
        _lastSeen = now;
    }

    private PlayRecord? MaybeRecordLocked(MediaSnapshot snapshot, out string status)
    {
        if (IsAutoRecording && !_recorded && snapshot.State == PlaybackState.Playing
            && _continuousSeconds >= _settings.RecordThresholdSeconds)
        {
            var id = _database.UpsertSong(snapshot, _coverPath);
            var duration = ResolveDuration(snapshot);
            _database.AddRecord(id, _startedAt, duration, isManual: false);
            _recorded = true;
            status = $"已自动记录：{snapshot.DisplayName}";
            return new PlayRecord
            {
                SongId = id,
                StartedAtUtc = _startedAt,
                RecordedAtUtc = DateTimeOffset.UtcNow,
                DurationSeconds = duration
            };
        }

        status = _continuousSeconds > 0.5
            ? $"{snapshot.StateLabel}：{snapshot.DisplayName}（连续 {Math.Round(_continuousSeconds)} 秒）"
            : $"{snapshot.StateLabel}：{snapshot.DisplayName}";
        return null;
    }

    /// <summary>优先记录整首时长，其次记录当前进度，最后退化为连续收听秒数。</summary>
    private int? ResolveDuration(MediaSnapshot snapshot)
    {
        if (snapshot.Duration is { TotalSeconds: > 0 } total) return (int)Math.Round(total.TotalSeconds);
        if (snapshot.Position is { TotalSeconds: > 0 } position) return (int)Math.Round(position.TotalSeconds);
        lock (_gate) return _continuousSeconds > 0.5 ? (int)Math.Round(_continuousSeconds) : null;
    }

    private async Task TryFetchCoverAsync(MediaSnapshot raw, CancellationToken cancellationToken)
    {
        if (_coverCache is null || !_settings.CaptureCoverArt) return;

        lock (_gate)
        {
            if (_coverAttempted) return;
            _coverAttempted = true;
        }

        var bytes = await _reader.ReadThumbnailAsync(raw.SourceApp, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0) return;
        var path = _coverCache.SaveFromBytes(bytes, raw.Title, raw.Artist, raw.Album);
        if (path is null) return;
        lock (_gate) _coverPath = path;
    }

    private void ResetSessionLocked()
    {
        _currentKey = null;
        _startedAt = default;
        _lastSeen = default;
        _continuousSeconds = 0;
        _recorded = false;
        _wasPaused = false;
        _lastPosition = null;
        _coverAttempted = false;
        _coverPath = null;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _tickGate.Dispose();
    }
}
