// The app shell's scripts, served as one fingerprinted, cached file (App.razor, <head>, render-blocking).
// Blocking on purpose: the theme, sidebar and backdrop attributes must be on <html> before first paint, and
// the capture-phase backdrop guard must be registered before blazor.web.js attaches its delegated handlers.
// The one thing that needs Blazor itself (the enhancedload listener) waits for DOMContentLoaded, by which
// time the blazor.web.js tag at the end of <body> has run.

(function () {
    // Cookie helpers shared by every preference below. readCookie returns the decoded value, or null when unset.
    function readCookie(name) {
        var m = document.cookie.match(new RegExp('(?:^|; )' + name + '=([^;]*)'));
        return m ? decodeURIComponent(m[1]) : null;
    }
    function writeCookie(name, value, maxAgeSeconds) {
        document.cookie = name + '=' + value + ';path=/;max-age=' + (maxAgeSeconds === undefined ? 31536000 : maxAgeSeconds) + ';samesite=lax';
    }

    (function () {
        function resolve(pref) {
            if (pref === 'dark' || pref === 'light') return pref;
            return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        function getPref() {
            var v = readCookie('ls-theme');
            return v === null ? 'auto' : v;
        }
        function apply() {
            document.documentElement.setAttribute('data-theme', resolve(getPref()));
        }
        apply();
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
            if (getPref() === 'auto') apply();
        });
        window.lsTheme = {
            set: function (pref) {
                writeCookie('ls-theme', encodeURIComponent(pref));
                apply();
            },
            get: getPref,
            apply: apply
        };
    })();

    (function () {
        function getPref() {
            return readCookie('ls-sidebar-collapsed') === 'true';
        }
        function apply() {
            document.documentElement.setAttribute('data-sidebar-collapsed', getPref() ? 'true' : 'false');
        }
        apply();
        window.lsSidebar = {
            toggle: function () {
                var next = !getPref();
                writeCookie('ls-sidebar-collapsed', next);
                apply();
            },
            apply: apply
        };
    })();

    (function () {
        function getPref() {
            return readCookie('ls-movie-backdrop') === 'true';
        }
        function apply() {
            document.documentElement.setAttribute('data-movie-backdrop', getPref() ? 'true' : 'false');
        }
        apply();
        window.lsMovieBackdrop = {
            set: function (pref) {
                writeCookie('ls-movie-backdrop', pref);
                apply();
            },
            get: getPref,
            apply: apply
        };
    })();

    (function () {
        // Seconds before an apex the player starts counting down; 0 = off.
        var DEFAULT = 10, MAX = 60;
        function get() {
            var v = readCookie('ls-apex-countdown');
            var n = v !== null ? parseInt(v, 10) : NaN;
            return isNaN(n) ? DEFAULT : Math.min(Math.max(n, 0), MAX);
        }
        window.lsApexCountdown = {
            get: get,
            set: function (seconds) {
                writeCookie('ls-apex-countdown', seconds);
            }
        };
    })();

    (function () {
        // Actor Detail's section order, comma-separated section names; "" = default.
        window.lsActorDetailSections = {
            get: function () {
                var v = readCookie('ls-actor-detail-sections');
                return v === null ? '' : v;
            },
            set: function (order) {
                writeCookie('ls-actor-detail-sections', encodeURIComponent(order));
            }
        };
        // Persists a page's remembered view state (name + JSON) - see JsonCookieState.SaveAsync.
        window.lsJsonCookie = {
            set: function (name, json, maxAgeSeconds) {
                writeCookie(name, encodeURIComponent(json), maxAgeSeconds);
            }
        };
        // The Movies page's poster options (MoviePosterOptions), also used by Actor Detail's grid.
        window.lsPosterOptions = {
            get: function () {
                return readCookie('poster-options');
            }
        };
    })();

    (function () {
        // Poster cards fade their image in over a blurred placeholder of the poster, its BlurHash (PosterCard.razor.css, and painted below).
        // An image is flagged `poster-pending` (hidden) only if it isn't already loaded the moment it
        // enters the DOM, and unflagged when it loads or fails, so one that is cached, already complete,
        // or kept across an enhanced navigation (which strips classes it doesn't render) is never left
        // hidden. load/error don't bubble, so they're caught in the capture phase on document.
        function flag(node) {
            if (node.nodeType !== 1) return;
            var imgs = node.matches('.poster-image img') ? [node] : node.querySelectorAll('.poster-image img');
            for (var i = 0; i < imgs.length; i++) {
                if (!imgs[i].complete) imgs[i].classList.add('poster-pending');
            }
        }
        function settle(e) {
            if (e.target instanceof HTMLImageElement) e.target.classList.remove('poster-pending');
        }
        document.addEventListener('load', settle, true);
        document.addEventListener('error', settle, true);
        // Behind the image, the card shows a blurred stand-in of the poster: its BlurHash (PosterCard puts it in
        // data-blurhash, and its average colour in the server-rendered background-color) decoded to a tiny
        // canvas and set as the background image, which the browser scales up smoothly. A background rather
        // than a child canvas, because Blazor patches this element's children by position and must not find
        // a foreign one. A hash is decoded once (hashes are per movie, so this mostly saves a card that mounts again).
        var B83 = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~';
        var BW = 12, BH = 18, blurCache = Object.create(null), blurCacheSize = 0;
        function dec83(s, a, b) {
            var v = 0;
            for (var i = a; i < b; i++) v = v * 83 + B83.indexOf(s.charAt(i));
            return v;
        }
        function toLinear(c) { c /= 255; return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }
        function toSrgb(v) {
            v = Math.max(0, Math.min(1, v));
            return Math.round((v <= 0.0031308 ? v * 12.92 : 1.055 * Math.pow(v, 1 / 2.4) - 0.055) * 255);
        }
        function signSquare(x) { return x * Math.abs(x); }
        function blurUrl(hash) {
            if (blurCache[hash]) return blurCache[hash];
            var size = dec83(hash, 0, 1), nx = size % 9 + 1, ny = Math.floor(size / 9) + 1;
            if (hash.length !== 6 + 2 * (nx * ny - 1)) return null;
            var max = (dec83(hash, 1, 2) + 1) / 166, colors = [], dc = dec83(hash, 2, 6);
            colors.push([toLinear(dc >> 16), toLinear((dc >> 8) & 255), toLinear(dc & 255)]);
            for (var c = 1; c < nx * ny; c++) {
                var q = dec83(hash, 4 + c * 2, 6 + c * 2);
                colors.push([
                    signSquare((Math.floor(q / 361) - 9) / 9) * max,
                    signSquare((Math.floor(q / 19) % 19 - 9) / 9) * max,
                    signSquare((q % 19 - 9) / 9) * max]);
            }
            var canvas = document.createElement('canvas');
            canvas.width = BW; canvas.height = BH;
            var ctx = canvas.getContext('2d');
            if (!ctx) return null;
            var img = ctx.createImageData(BW, BH);
            // The cosines depend only on pixel and component, so they're tabled once per hash (~80) rather
            // than computed per pixel and component (~2,600).
            var cosX = [], cosY = [], i, j;
            for (i = 0; i < nx; i++) { cosX[i] = []; for (var x0 = 0; x0 < BW; x0++) cosX[i][x0] = Math.cos(Math.PI * x0 * i / BW); }
            for (j = 0; j < ny; j++) { cosY[j] = []; for (var y0 = 0; y0 < BH; y0++) cosY[j][y0] = Math.cos(Math.PI * y0 * j / BH); }
            for (var y = 0; y < BH; y++) {
                for (var x = 0; x < BW; x++) {
                    var r = 0, g = 0, b = 0;
                    for (j = 0; j < ny; j++) {
                        for (i = 0; i < nx; i++) {
                            var basis = cosX[i][x] * cosY[j][y];
                            var col = colors[j * nx + i];
                            r += col[0] * basis; g += col[1] * basis; b += col[2] * basis;
                        }
                    }
                    var o = 4 * (y * BW + x);
                    img.data[o] = toSrgb(r); img.data[o + 1] = toSrgb(g); img.data[o + 2] = toSrgb(b); img.data[o + 3] = 255;
                }
            }
            ctx.putImageData(img, 0, 0);
            if (blurCacheSize >= 2000) { blurCache = Object.create(null); blurCacheSize = 0; }
            blurCacheSize++;
            return (blurCache[hash] = 'url(' + canvas.toDataURL() + ')');
        }
        function paint(el) {
            var hash = el.getAttribute('data-blurhash');
            if (!hash) return;
            // Reused for another movie, or Blazor rewrote the style attribute: repaint; otherwise nothing to do.
            if (el.__blurHash === hash && el.style.backgroundImage) return;
            try {
                var url = blurUrl(hash);
                if (!url) return;
                el.style.backgroundImage = url;
                el.style.backgroundSize = '100% 100%';
                el.__blurHash = hash;
            } catch (e) { /* the plain colour stays */ }
        }
        function paintWithin(node) {
            if (node.nodeType !== 1) return;
            if (node.matches('.poster-image[data-blurhash]')) paint(node);
            var els = node.querySelectorAll('.poster-image[data-blurhash]');
            for (var i = 0; i < els.length; i++) paint(els[i]);
        }
        new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                if (records[i].type === 'attributes') {
                    paintWithin(records[i].target);
                    continue;
                }
                for (var j = 0; j < records[i].addedNodes.length; j++) {
                    flag(records[i].addedNodes[j]);
                    paintWithin(records[i].addedNodes[j]);
                }
            }
        }).observe(document, { childList: true, subtree: true, attributes: true, attributeFilter: ['data-blurhash', 'style'] });
    })();

    (function () {
        // A drag that starts inside a dialog (selecting text in an input) and is released over its backdrop
        // is reported as a click on the backdrop, which would close the dialog and discard the edit. A
        // backdrop marked data-backdrop-dismiss only gets the click when the press began on it too. Capture
        // phase on window, so it runs before Blazor's own delegated click handler.
        var pressTarget = null;
        window.addEventListener('mousedown', function (e) { pressTarget = e.target; }, true);
        window.addEventListener('click', function (e) {
            var started = pressTarget;
            pressTarget = null;
            if (started && e.target instanceof Element && e.target.hasAttribute('data-backdrop-dismiss') && started !== e.target) {
                e.stopImmediatePropagation();
            }
        }, true);
    })();

    // Needs window.Blazor, which blazor.web.js (the last script in <body>) defines.
    document.addEventListener('DOMContentLoaded', function () {
        if (window.Blazor && window.Blazor.addEventListener) {
            window.Blazor.addEventListener('enhancedload', function () {
                window.lsTheme.apply();
                window.lsSidebar.apply();
                window.lsMovieBackdrop.apply();
                restoreGridScrollEarly();
            });
        }
    });

    // A back-navigation's markup arrives with spacers of height 0 (the server can't know the
    // row height), so the page is too short to scroll until VirtualizedGrid.razor.js has
    // loaded and measured - and the viewport sat at the top meanwhile. Reuse
    // the spacer heights and scroll position that module saved last time to restore at once;
    // it still re-measures and corrects both when it starts.
    function restoreGridScrollEarly() {
        try {
            var leading = document.querySelector('.virtualized-grid-spacer[data-scroll-storage-key]');
            if (!leading) return;
            var key = leading.getAttribute('data-scroll-storage-key');
            var saved = Number(sessionStorage.getItem(key));
            var spacers = JSON.parse(sessionStorage.getItem(key + ':spacers'));
            if (!saved || !spacers || String(spacers.windowStart) !== leading.getAttribute('data-window-start')) return;
            var trailing = leading.parentElement.querySelector('.virtualized-grid-spacer:last-child');
            leading.style.height = spacers.leading;
            trailing.style.height = spacers.trailing;
            window.scrollTo({ top: saved, behavior: 'instant' });
        } catch (e) { }
    }
})();
