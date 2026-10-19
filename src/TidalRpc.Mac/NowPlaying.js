// Run by /usr/bin/osascript, which macOS allows to read Now Playing (MediaRemote).
// Prints one JSON line per change, not per poll, so the app stays idle between songs.
// Exits after a while so a long-lived JavaScriptCore heap never grows; the app restarts it.
ObjC.import('Foundation');
ObjC.import('stdlib');

function run() {
    const out = $.NSFileHandle.fileHandleWithStandardOutput;
    const write = value => out.writeData($(JSON.stringify(value) + '\n').dataUsingEncoding($.NSUTF8StringEncoding));
    if (!$.NSBundle.bundleWithPath('/System/Library/PrivateFrameworks/MediaRemote.framework').load) { write({ error: 'unavailable' }); $.exit(2); }
    const request = $.NSClassFromString('MRNowPlayingRequest');
    if (request.isNil()) { write({ error: 'unsupported' }); $.exit(2); }

    const text = value => { try { const v = ObjC.unwrap(value); return typeof v === 'string' ? v : ''; } catch (e) { return ''; } };
    const number = value => { try { const v = ObjC.unwrap(value); return typeof v === 'number' && isFinite(v) ? v : null; } catch (e) { return null; } };

    function read() {
        const item = request.localNowPlayingItem;
        if (item.isNil()) return {};
        const info = item.nowPlayingInfo;
        if (info.isNil()) return {};
        const key = name => info.valueForKey('kMRMediaRemoteNowPlayingInfo' + name);
        let client = $();
        try { client = request.localNowPlayingPlayerPath.client; } catch (e) { }
        const field = name => { try { return client.isNil() ? '' : text(client[name]); } catch (e) { return ''; } };
        const rate = number(key('PlaybackRate'));
        let position = number(key('ElapsedTime'));
        const stamp = key('Timestamp');
        // ElapsedTime is measured at Timestamp; advance it to now while playing.
        if (position !== null && rate > 0 && !stamp.isNil()) position += -stamp.timeIntervalSinceNow * rate;
        if (position !== null) position = Math.max(0, position);
        let playing = rate !== null ? rate > 0 : false;
        if (rate === null) { try { playing = request.localIsPlaying === true || ObjC.unwrap(request.localIsPlaying) === true; } catch (e) { } }
        return {
            app: field('bundleIdentifier'), parent: field('parentApplicationBundleIdentifier'), name: field('displayName'),
            title: text(key('Title')), artist: text(key('Artist')), album: text(key('Album')),
            duration: number(key('Duration')), position, playing
        };
    }

    let last = null, lastStart = null, lastPosition = null;
    for (let i = 0; i < 1800; i++) {
        let now;
        try { now = read(); } catch (e) { now = {}; }
        const { position, ...identity } = now;
        const id = JSON.stringify(identity);
        const start = position !== null && position !== undefined ? Date.now() / 1000 - position : null;
        // Report seeks, not the clock: a playing track's start time or a paused position moved by > 2 s.
        const moved = now.playing ? (start === null) !== (lastStart === null) || Math.abs(start - lastStart) > 2
            : (position ?? null) !== null && Math.abs(position - lastPosition) > 2;
        if (id !== last || moved) {
            write(now);
            last = id; lastStart = start; lastPosition = position ?? null;
        }
        delay(1);
    }
}
