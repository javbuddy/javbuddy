// Owns the client-side behaviour of the Movie Cleanup scrub bar: hovering the timeline shows the
// trickplay thumbnail for that moment, clicking seeks the video, and the playhead follows playback.
// It runs entirely in the browser; a preview per pointer move over SignalR would lag.
//
// `config` is the serialized TrickplayLayout (camelCase): thumbnail width/height, tileWidth/tileHeight
// thumbnails per sheet, thumbnailCount, intervalMs, durationSeconds, tileUrlTemplate with an {index}
// placeholder, and startSeconds (where the first thumbnail is: a highlight's own set, else 0). A movie
// without trickplay passes just { durationSeconds } and the preview shows only the time.

const PREVIEW_WIDTH_PX = 200;
const CONTROLS_STRIP_PX = 48;

const instances = new WeakMap();

export function init(root, config, videoSelector) {
    dispose(root);
    if (!root || !config || !(config.durationSeconds > 0)) return;

    const track = root.querySelector('.scrub-bar-track');
    const progress = root.querySelector('.scrub-bar-progress');
    const preview = root.querySelector('.scrub-bar-preview');
    const thumb = root.querySelector('.scrub-bar-thumb');
    const label = root.querySelector('.scrub-bar-time');
    const fullscreenBtn = root.querySelector('.scrub-bar-fullscreen');
    const scrubBar = root.querySelector('.scrub-bar-track-wrap') || root;
    if (!track || !progress || !preview || !label) return;

    const showThumbnail = thumb && config.tileUrlTemplate ? trickplayThumbnails(thumb, config) : noThumbnails;

    const secondsAt = (clientX) => {
        const rect = track.getBoundingClientRect();
        const fraction = rect.width > 0 ? (clientX - rect.left) / rect.width : 0;
        return Math.min(Math.max(fraction, 0), 1) * config.durationSeconds;
    };

    // The scene under the pointer, read from SceneSegments' data-* attributes on each
    // hover so it follows scene edits without re-initialising. Ranges are [start, end).
    const sceneNameAt = (seconds) => {
        for (const segment of track.querySelectorAll('.scene-segment[data-scene-name]')) {
            const start = Number(segment.dataset.start);
            const end = Number(segment.dataset.end);
            if (seconds >= start && seconds < end) return segment.dataset.sceneName;
        }
        return null;
    };

    // A highlight span or apex marker under the pointer: hovering
    // previews, and clicking seeks to, its start rather than the pointer's position.
    const highlightAt = (event) => event.target instanceof Element ? event.target.closest('.highlight-segment[data-start], .apex-marker[data-start]') : null;

    const showPreview = (event) => {
        const highlight = highlightAt(event);
        const seconds = highlight ? Number(highlight.dataset.start) : secondsAt(event.clientX);
        showThumbnail(seconds);
        const name = highlight ? highlight.dataset.highlightName : sceneNameAt(seconds);
        label.textContent = name ? `${formatTime(seconds)} · ${name}` : formatTime(seconds);

        placePreview(preview, track, event.clientX);
        preview.classList.add('scrub-bar-preview-visible');
    };

    const hidePreview = () => preview.classList.remove('scrub-bar-preview-visible');
    const preloadSheets = () => showThumbnail.preload();

    const seek = (event) => {
        const video = document.querySelector(videoSelector);
        if (!video) return;
        const highlight = highlightAt(event);
        video.currentTime = highlight ? Number(highlight.dataset.start) : secondsAt(event.clientX);
        if (video.paused) {
            video.play().catch(() => { });
        }
    };

    // `timeupdate` doesn't bubble, so listen in the capture phase on the document and filter to
    // our video — that also keeps working if the <video> element is replaced between cards.
    const onTimeUpdate = (event) => {
        if (!(event.target instanceof Element) || !event.target.matches(videoSelector)) return;
        const duration = event.target.duration > 0 ? event.target.duration : config.durationSeconds;
        progress.style.width = `${Math.min((event.target.currentTime / duration) * 100, 100)}%`;
    };

    // Play/pause, the time readout and mute + volume for a player without native controls
    //, as ClipControls.razor.js does for a clip. Clicking the video and Space play/pause
    // too, unless a field has focus or a dialog is open over the player. Space is claimed in the capture
    // phase even with a button focused, like Review mode's (MovieCleanup.razor.js): otherwise it pressed
    // the scene editor's last clicked button again instead of playing/pausing.
    function bindTransport(bar) {
        const video = document.querySelector(videoSelector);
        if (!video) return () => { };
        const playBtn = bar.querySelector('.scrub-bar-play');
        const muteBtn = bar.querySelector('.scrub-bar-mute');
        const volume = bar.querySelector('.scrub-bar-volume');
        const clock = bar.querySelector('.scrub-bar-clock');

        const render = () => {
            const duration = video.duration > 0 ? video.duration : config.durationSeconds;
            clock.textContent = `${formatTime(video.currentTime)} / ${formatTime(duration)}`;
            bar.classList.toggle('scrub-bar-playing', !video.paused);
            bar.classList.toggle('scrub-bar-muted', video.muted || video.volume === 0);
            volume.value = video.muted ? 0 : video.volume;
        };
        const togglePlay = () => {
            if (video.paused) {
                video.play().catch(() => { });
            } else {
                video.pause();
            }
        };
        const onMute = () => {
            video.muted = !video.muted;
            if (!video.muted && video.volume === 0) video.volume = 1;
        };
        const onVolume = () => {
            video.volume = Number(volume.value);
            video.muted = video.volume === 0;
        };
        const isEditable = (element) => element instanceof Element
            && (element.closest('input, textarea, select') !== null || element.isContentEditable);
        const onKeyDown = (event) => {
            if (event.key !== ' ' || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
            if (isEditable(event.target) || isEditable(document.activeElement)) return;
            if (document.querySelector('.image-lightbox-backdrop, .cleanup-modal-overlay, .delete-confirm-backdrop')) return;
            event.preventDefault();
            togglePlay();
        };
        // A mouse click leaves focus where it was, so Space keeps playing/pausing instead of pressing
        // the last clicked button again.
        const keepFocus = (event) => {
            if (event.target instanceof Element && event.target.closest('button')) event.preventDefault();
        };

        const listeners = [
            [video, 'click', togglePlay],
            ...['play', 'pause', 'timeupdate', 'volumechange', 'loadedmetadata', 'durationchange', 'seeked'].map(type => [video, type, render]),
            [playBtn, 'click', togglePlay],
            [muteBtn, 'click', onMute],
            [volume, 'input', onVolume],
            [bar, 'mousedown', keepFocus],
            [window, 'keydown', onKeyDown, true],
        ];
        for (const [target, type, handler, capture] of listeners) target.addEventListener(type, handler, capture);
        render();
        const unbind = () => {
            for (const [target, type, handler, capture] of listeners) target.removeEventListener(type, handler, capture);
        };
        unbind.bound = true;
        return unbind;
    }

    const unbindTransport = root.querySelector('.scrub-bar-play') ? bindTransport(root) : () => { };

    scrubBar.addEventListener('pointerenter', preloadSheets);
    scrubBar.addEventListener('pointermove', showPreview);
    scrubBar.addEventListener('pointerleave', hidePreview);
    scrubBar.addEventListener('click', seek);
    document.addEventListener('timeupdate', onTimeUpdate, true);

    const video = document.querySelector(videoSelector);
    const container = root.closest('.cleanup-player-column, .video-player-column') || root.parentElement;
    // Without native controls there's no strip at the video's bottom that a double-click must skip.
    const unbindFullscreen = bindFullscreen(root, container, video, fullscreenBtn, 'scrub-bar-is-fullscreen', unbindTransport.bound ? 0 : CONTROLS_STRIP_PX);

    instances.set(root, () => {
        scrubBar.removeEventListener('pointerenter', preloadSheets);
        scrubBar.removeEventListener('pointermove', showPreview);
        scrubBar.removeEventListener('pointerleave', hidePreview);
        scrubBar.removeEventListener('click', seek);
        document.removeEventListener('timeupdate', onTimeUpdate, true);
        unbindFullscreen();
        unbindTransport();
    });
}

export function dispose(root) {
    const teardown = root && instances.get(root);
    if (teardown) {
        teardown();
        instances.delete(root);
    }
}

function noThumbnails() { }
noThumbnails.preload = () => { };

// Decoded sheets kept per thumb, in bytes: enough to keep every sheet of a movie up to 4 hours
// decoded at once (15 sheets at 10 s intervals), so scrubbing never waits on one again. At 1x a
// sheet of 200 px thumbnails is ~9 MB, so they fit at full quality; at 2x the thumbnails are
// decoded smaller than the canvas's 400 px to fit (~223 px for 4 hours, ~305 px for 2 hours).
const DECODED_BUDGET_BYTES = 160 * 1024 * 1024;
const PRELOAD_CONCURRENCY = 2;

// The latest trickplayThumbnails() call for each thumb, so a sheet that finishes decoding after the
// layout was swapped (ClipControls' setTrickplay) doesn't draw the old movie's thumbnail, and the
// old call's decoded sheets are freed.
const thumbOwners = new WeakMap();

// Sizes `thumb` for the trickplay layout in `config` and returns a function that shows the
// thumbnail for a point in the video. Also used by ClipControls.razor.js and
// MovieScenesSection.razor.js.
//
// Thumbnails are drawn on a <canvas> from sheets decoded into ImageBitmaps at the size they're shown,
// so any thumbnail of a decoded sheet is instant; until a sheet is decoded the canvas keeps the
// previous thumbnail. The sheets nearest the pointer are decoded ahead of it, one at a time, at a size
// that fits all the layout's sheets in DECODED_BUDGET_BYTES. Only a layout too long for that even at
// 1x (past ~5 hours at 10 s intervals) drops the farthest decoded sheet and decodes it again when the
// pointer returns.
//
// The returned function's `preload(fromSeconds, toSeconds)` downloads every sheet covering that
// range at low priority, nearest the pointer first. Callers start it on the first hover, so a player
// that's never scrubbed costs nothing.
export function trickplayThumbnails(thumb, config) {
    thumbOwners.get(thumb)?.release();
    const owner = { release: () => { } };
    thumbOwners.set(thumb, owner);
    const isCurrent = () => thumbOwners.get(thumb) === owner;

    const thumbsPerSheet = config.tileWidth * config.tileHeight;
    const width = PREVIEW_WIDTH_PX;
    const height = Math.round(config.height * (PREVIEW_WIDTH_PX / config.width));
    thumb.style.width = `${width}px`;
    thumb.style.height = `${height}px`;

    // Kept across layouts of the same size, so the previous thumbnail stays up until the new
    // layout's sheet is decoded.
    let canvas = thumb.querySelector(':scope > canvas');
    if (!canvas) {
        canvas = document.createElement('canvas');
        canvas.style.cssText = 'display:block;width:100%;height:100%';
        thumb.append(canvas);
    }
    const dpr = Math.max(window.devicePixelRatio || 1, 1);
    const [canvasWidth, canvasHeight] = [Math.round(width * dpr), Math.round(height * dpr)];
    if (canvas.width !== canvasWidth || canvas.height !== canvasHeight) {
        canvas.width = canvasWidth;
        canvas.height = canvasHeight;
    }
    const context = canvas.getContext('2d');
    // Sheets are decoded at the canvas's resolution, never above their own, and smaller where
    // that's needed to keep all of them decoded, though not below the 1x preview's.
    const sheetCount = Math.ceil(config.thumbnailCount / thumbsPerSheet);
    const fullSheetBytes = config.tileWidth * config.width * config.tileHeight * config.height * 4;
    const fitAllScale = Math.sqrt(DECODED_BUDGET_BYTES / (sheetCount * fullSheetBytes));
    const scale = Math.min(canvasWidth / config.width, 1, Math.max(fitAllScale, width / config.width));
    const maxDecoded = scale <= fitAllScale ? sheetCount : Math.max(Math.floor(DECODED_BUDGET_BYTES / (fullSheetBytes * scale * scale)), 2);

    const startSeconds = config.startSeconds ?? 0;
    const thumbIndexAt = (seconds) => Math.min(Math.max(Math.floor(((seconds - startSeconds) * 1000) / config.intervalMs), 0), config.thumbnailCount - 1);
    const sheetUrl = (sheetIndex) => config.tileUrlTemplate.replace('{index}', sheetIndex);

    // By sheet index: { blob: Promise<Blob>, bitmap, failed }.
    const sheets = new Map();
    let wanted = null;
    let range = null;

    owner.release = () => {
        for (const sheet of sheets.values()) sheet.bitmap?.close();
        sheets.clear();
    };

    const draw = (index) => {
        const sheet = sheets.get(Math.floor(index / thumbsPerSheet));
        if (!sheet?.bitmap || !isCurrent()) return;
        const withinSheet = index % thumbsPerSheet;
        const cellWidth = sheet.bitmap.width / config.tileWidth;
        const cellHeight = sheet.bitmap.height / config.tileHeight;
        context.drawImage(sheet.bitmap,
            (withinSheet % config.tileWidth) * cellWidth, Math.floor(withinSheet / config.tileWidth) * cellHeight, cellWidth, cellHeight,
            0, 0, canvasWidth, canvasHeight);
        thumb.dataset.sheet = sheetUrl(Math.floor(index / thumbsPerSheet));
    };

    const sheetEntry = (sheetIndex) => {
        let sheet = sheets.get(sheetIndex);
        if (!sheet) {
            sheet = { blob: null, bitmap: null, failed: false };
            sheets.set(sheetIndex, sheet);
        }
        return sheet;
    };

    const download = (sheetIndex, priority) => {
        const sheet = sheetEntry(sheetIndex);
        sheet.blob ??= fetch(sheetUrl(sheetIndex), { priority }).then((response) => {
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return response.blob();
        });
        return sheet;
    };

    // Jellyfin's tiles (the fallback) come from its own origin; should a proxy strip its CORS
    // headers, an <img> still loads them.
    const decodeFull = async (sheetIndex, sheet) => {
        try {
            return await createImageBitmap(await sheet.blob);
        } catch {
            const image = new Image();
            image.src = sheetUrl(sheetIndex);
            await image.decode();
            return createImageBitmap(image);
        }
    };

    const decode = async (sheetIndex) => {
        const sheet = download(sheetIndex, 'high');
        try {
            const full = await decodeFull(sheetIndex, sheet);
            const bitmap = scale < 1
                ? await createImageBitmap(full, { resizeWidth: Math.round(full.width * scale), resizeHeight: Math.round(full.height * scale), resizeQuality: 'medium' })
                : full;
            if (bitmap !== full) full.close();
            if (!isCurrent() || sheets.get(sheetIndex) !== sheet) {
                bitmap.close();
                return;
            }
            sheet.bitmap = bitmap;
            if (wanted !== null && Math.floor(wanted / thumbsPerSheet) === sheetIndex) draw(wanted);
        } catch {
            sheet.failed = true;
        }
    };

    // The wanted sheet first, then the preloaded range's sheets nearest to it.
    const focusSheet = () => (wanted === null ? (range?.[0] ?? 0) : Math.floor(wanted / thumbsPerSheet));
    const byDistance = () => {
        const focus = focusSheet();
        const candidates = [focus];
        if (range) {
            for (let i = range[0]; i <= range[1]; i++) {
                if (i !== focus) candidates.push(i);
            }
            candidates.sort((a, b) => Math.abs(a - focus) - Math.abs(b - focus));
        }
        return candidates;
    };

    let downloading = 0;
    const downloadAhead = () => {
        while (range && isCurrent() && downloading < PRELOAD_CONCURRENCY) {
            const next = byDistance().find((i) => !sheets.get(i)?.blob);
            if (next === undefined) return;
            downloading++;
            download(next, 'low').blob.catch(() => { }).finally(() => {
                downloading--;
                downloadAhead();
            });
        }
    };

    const nextToDecode = () => {
        const focus = focusSheet();
        const candidates = byDistance();
        const pending = candidates.find((i) => !sheets.get(i)?.bitmap && !sheets.get(i)?.failed);
        if (pending === undefined) return null;

        const decoded = [...sheets.entries()].filter(([, sheet]) => sheet.bitmap);
        if (decoded.length < maxDecoded) return pending;
        const [farthest, farthestSheet] = decoded.reduce((a, b) => (Math.abs(b[0] - focus) > Math.abs(a[0] - focus) ? b : a));
        if (Math.abs(farthest - focus) <= Math.abs(pending - focus)) return null;
        farthestSheet.bitmap.close();
        farthestSheet.bitmap = null;
        return pending;
    };

    // One decode at a time, so at most one full-size sheet is in memory besides the kept ones.
    let decoding = false;
    const pump = async () => {
        if (decoding) return;
        decoding = true;
        try {
            let next;
            while (isCurrent() && (next = nextToDecode()) !== null) await decode(next);
        } finally {
            decoding = false;
        }
    };

    const show = (seconds) => {
        wanted = thumbIndexAt(seconds);
        draw(wanted);
        pump();
    };

    show.preload = (fromSeconds = startSeconds, toSeconds = startSeconds + config.durationSeconds) => {
        if (range) return;
        range = [Math.floor(thumbIndexAt(fromSeconds) / thumbsPerSheet), Math.min(Math.floor(thumbIndexAt(toSeconds) / thumbsPerSheet), sheetCount - 1)];
        downloadAhead();
        pump();
    };

    return show;
}

// Centers the hover preview on the pointer, kept inside the track.
export function placePreview(preview, track, clientX) {
    const rect = track.getBoundingClientRect();
    const half = PREVIEW_WIDTH_PX / 2;
    const x = Math.min(Math.max(clientX - rect.left, half), Math.max(rect.width - half, half));
    preview.style.left = `${x}px`;
}

// Fullscreen for a player column: the button, the `f` key, double-clicking the video (above its
// bottom `controlsStripPx`, where native controls sit) and the video's own fullscreen request all
// toggle `container`, so the controls under the video stay visible. `root` gets `fullscreenClass`
// while it's fullscreen. Entering fullscreen moves focus to <body>, so leaving it hands focus back
// to whatever had it — the player's Esc-to-close handler listens on the focused backdrop
//. Returns the teardown. Also used by ClipControls.razor.js.
export function bindFullscreen(root, container, video, button, fullscreenClass, controlsStripPx) {
    const isFullscreen = () => document.fullscreenElement === container;

    let focusBeforeFullscreen = null;
    const requestFullscreen = (options, fallback) => {
        const req = container?.requestFullscreen || container?.webkitRequestFullscreen;
        if (!req) return fallback ? fallback(options) : Promise.resolve();
        focusBeforeFullscreen = document.activeElement;
        return req.call(container, options);
    };

    const toggleFullscreen = () => {
        if (isFullscreen()) {
            if (document.exitFullscreen) {
                document.exitFullscreen().catch(() => {});
            }
        } else {
            requestFullscreen().catch(() => {});
        }
    };

    const onFullscreenClick = (event) => {
        event.stopPropagation();
        toggleFullscreen();
    };
    button?.addEventListener('click', onFullscreenClick);

    const onDblClick = (event) => {
        if (!video) return;
        if (event.clientY > video.getBoundingClientRect().bottom - controlsStripPx) return;
        toggleFullscreen();
    };
    video?.addEventListener('dblclick', onDblClick);

    const onKeyDown = (event) => {
        if (event.key === 'f' || event.key === 'F') {
            if (['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName)) return;
            event.preventDefault();
            toggleFullscreen();
        }
    };
    window.addEventListener('keydown', onKeyDown);

    const onFullscreenChange = () => {
        const fs = isFullscreen();
        root.classList.toggle(fullscreenClass, fs);
        if (button) {
            button.setAttribute('title', fs ? 'Exit fullscreen (Esc, double-click)' : 'Toggle fullscreen (f, double-click video)');
            button.setAttribute('aria-label', fs ? 'Exit fullscreen' : 'Toggle fullscreen');
        }
        if (!fs && focusBeforeFullscreen) {
            if (focusBeforeFullscreen.isConnected) focusBeforeFullscreen.focus({ preventScroll: true });
            focusBeforeFullscreen = null;
        }
    };
    document.addEventListener('fullscreenchange', onFullscreenChange);

    let originalRequestFullscreen = null;
    if (video) {
        originalRequestFullscreen = video.requestFullscreen;
        video.requestFullscreen = (options) => requestFullscreen(options,
            originalRequestFullscreen && ((o) => originalRequestFullscreen.call(video, o)));
    }

    return () => {
        button?.removeEventListener('click', onFullscreenClick);
        video?.removeEventListener('dblclick', onDblClick);
        window.removeEventListener('keydown', onKeyDown);
        document.removeEventListener('fullscreenchange', onFullscreenChange);
        if (video && originalRequestFullscreen) {
            video.requestFullscreen = originalRequestFullscreen;
        }
    };
}

export function formatTime(totalSeconds) {
    const s = Math.floor(totalSeconds);
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = s % 60;
    const pad = (n) => String(n).padStart(2, '0');
    return h > 0 ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
}
