using Windows.Media.Control;

namespace TingGeRiZhi.Core;

/// <summary>通过 Windows 原生媒体会话（GSMTC）读取当前播放信息。</summary>
public sealed class MediaSessionReader
{
    /// <summary>封面缩略图上限，超过则不读取，避免大图拖慢轮询。</summary>
    private const ulong MaxThumbnailBytes = 8 * 1024 * 1024;

    /// <summary>读取系统认为"当前"的媒体会话（不含封面，封面由 <see cref="ReadThumbnailAsync"/> 单独抓取）。</summary>
    public async Task<MediaSnapshot> ReadCurrentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var session = manager.GetCurrentSession();
            if (session is null) return Unavailable("未找到活动的 Windows 媒体会话");
            return await ReadSessionAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            return Unavailable($"读取失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 在指定来源应用的会话中挑一个读取（找不到返回 null）。
    /// 同一应用可能有多个会话时优先取正在播放的那个。
    /// </summary>
    public async Task<MediaSnapshot?> ReadPreferredAsync(Func<string, bool> sourcePredicate, CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();

            GlobalSystemMediaTransportControlsSession? chosen = null;
            foreach (var session in manager.GetSessions())
            {
                if (!sourcePredicate(session.SourceAppUserModelId ?? "")) continue;
                var status = session.GetPlaybackInfo().PlaybackStatus;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    chosen = session;
                    break;
                }
                chosen ??= session;
            }

            if (chosen is null) return null;
            return await ReadSessionAsync(chosen, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>列出当前所有媒体会话，用于排查"到底哪个应用在被记录"。</summary>
    public async Task<IReadOnlyList<MediaSessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<MediaSessionSummary>();
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var currentSource = manager.GetCurrentSession()?.SourceAppUserModelId ?? "";

            foreach (var session in manager.GetSessions())
            {
                try
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    var playback = session.GetPlaybackInfo();
                    var source = session.SourceAppUserModelId ?? "";
                    result.Add(new MediaSessionSummary(
                        source,
                        props?.Title?.Trim() ?? "",
                        props?.Artist?.Trim() ?? "",
                        ToState(playback.PlaybackStatus),
                        string.Equals(source, currentSource, StringComparison.Ordinal)));
                }
                catch (Exception)
                {
                    // 单个会话读取失败不影响其它会话。
                }
            }
        }
        catch (Exception)
        {
            // 没有会话管理器时返回空列表。
        }
        return result;
    }

    private static async Task<MediaSnapshot> ReadSessionAsync(
        GlobalSystemMediaTransportControlsSession session,
        CancellationToken cancellationToken)
    {
        var props = await session.TryGetMediaPropertiesAsync();
        var timeline = session.GetTimelineProperties();
        var playback = session.GetPlaybackInfo();
        var state = ToState(playback.PlaybackStatus);
        var source = session.SourceAppUserModelId ?? "";

        var title = props?.Title?.Trim() ?? "";
        var artist = props?.Artist?.Trim() ?? "";
        var album = props?.AlbumTitle?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
        {
            return new MediaSnapshot("", "", album, state, SafePosition(timeline), SafeDuration(timeline),
                source, DateTimeOffset.UtcNow, false, "媒体会话未提供歌曲标题");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new MediaSnapshot(title, artist, album, state, SafePosition(timeline), SafeDuration(timeline),
            source, DateTimeOffset.UtcNow, true);
    }

    private static MediaSnapshot Unavailable(string error) =>
        new("", "", "", PlaybackState.Closed, null, null, "", DateTimeOffset.UtcNow, false, error);

    private static PlaybackState ToState(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackState.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => PlaybackState.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => PlaybackState.Stopped,
        _ => PlaybackState.Unknown
    };

    /// <summary>读取当前会话的封面缩略图原始字节；任何失败都返回 null（封面永远不是关键路径）。</summary>
    public async Task<byte[]?> ReadThumbnailAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var session = manager.GetCurrentSession();
            if (session is null) return null;
            return await ReadThumbnailAsync(session, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>读取指定来源会话的封面缩略图。</summary>
    public async Task<byte[]?> ReadThumbnailAsync(string? preferredSourceApp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(preferredSourceApp)) return await ReadThumbnailAsync(cancellationToken);

        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var session = manager.GetSessions()
                .FirstOrDefault(s => string.Equals(s.SourceAppUserModelId ?? "", preferredSourceApp, StringComparison.Ordinal));
            if (session is null) return await ReadThumbnailAsync(cancellationToken);
            return await ReadThumbnailAsync(session, cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(
        GlobalSystemMediaTransportControlsSession session,
        CancellationToken cancellationToken)
    {
        var props = await session.TryGetMediaPropertiesAsync();
        if (props is null) return null;

        try
        {
            var reference = props.Thumbnail;
            if (reference is null) return null;
            using var stream = await reference.OpenReadAsync();
            if (stream is null || stream.Size == 0 || stream.Size > MaxThumbnailBytes) return null;
            cancellationToken.ThrowIfCancellationRequested();

            var size = (uint)stream.Size;
            var buffer = new byte[size];
            using var reader = new Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync(size);
            reader.ReadBytes(buffer);
            return buffer;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TimeSpan? SafePosition(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        var value = timeline.Position;
        return value < TimeSpan.Zero ? null : value;
    }

    private static TimeSpan? SafeDuration(GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        var value = timeline.EndTime - timeline.StartTime;
        return value <= TimeSpan.Zero ? null : value;
    }
}
