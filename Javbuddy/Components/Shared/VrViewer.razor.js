// Owns the client-side behaviour of the Movie Cleanup "VR 2D" mode: it draws the left eye of a
// side-by-side (SBS) VR <video> onto a WebGL canvas through a virtual camera, so the
// fisheye/equirectangular warp is undone and dragging looks around. It runs entirely in the browser;
// a redraw per pointer move over SignalR would lag.
//
// Assumptions (best-effort by design): the frame is left-eye | right-eye, each eye covers 180°
// (fisheye: the image circle fills the eye's box; equirectangular: the eye box spans 180° x 180°).
// Top-bottom layouts and lenses wider than 180° are not handled.
//
// The video is Javbuddy's own same-origin stream, so WebGL can sample its frames directly.

const VERTICAL_FOV_DEGREES = 75;
const DRAG_THRESHOLD_PX = 4;
// The strip at the bottom of the <video> where the browser draws its native controls; presses
// there are left to the browser instead of starting a drag.
const CONTROLS_STRIP_PX = 56;
const MAX_DEVICE_PIXEL_RATIO = 2;

const instances = new WeakMap();

const VERTEX_SHADER = `
attribute vec2 aPos;
varying vec2 vNdc;
void main() {
    vNdc = aPos;
    gl_Position = vec4(aPos, 0.0, 1.0);
}`;

// For each output pixel: build the camera ray, rotate it by the look direction (pitch, then yaw
// about the world's up axis), then find where that ray lands in the left eye's image.
const FRAGMENT_SHADER = `
precision highp float;
varying vec2 vNdc;
uniform sampler2D uVideo;
uniform vec2 uLook;        // yaw, pitch in radians (positive = right, up)
uniform vec2 uTanHalfFov;  // tan of the half field of view, horizontal and vertical
uniform float uProjection; // 0 = fisheye, 1 = equirectangular
const float PI = 3.14159265359;

void main() {
    vec3 ray = normalize(vec3(vNdc * uTanHalfFov, 1.0));
    float cp = cos(uLook.y), sp = sin(uLook.y);
    ray = vec3(ray.x, ray.y * cp + ray.z * sp, -ray.y * sp + ray.z * cp);
    float cy = cos(uLook.x), sy = sin(uLook.x);
    ray = vec3(ray.x * cy + ray.z * sy, ray.y, -ray.x * sy + ray.z * cy);

    // e = position within the left eye's box, -1..1 on both axes.
    vec2 e;
    if (uProjection < 0.5) {
        // Equidistant fisheye: distance from the image centre is proportional to the angle off-axis.
        float theta = acos(clamp(ray.z, -1.0, 1.0));
        vec2 dir = length(ray.xy) > 0.0 ? normalize(ray.xy) : vec2(0.0);
        e = dir * (theta / (PI * 0.5));
    } else {
        e = vec2(atan(ray.x, ray.z), asin(clamp(ray.y, -1.0, 1.0))) / (PI * 0.5);
    }

    if (ray.z <= 0.0 || abs(e.x) > 1.0 || abs(e.y) > 1.0 || (uProjection < 0.5 && length(e) > 1.0)) {
        gl_FragColor = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }
    // Left eye = left half of the frame; texture row 0 is the top of the video.
    gl_FragColor = texture2D(uVideo, vec2(0.25 + e.x * 0.25, 0.5 - e.y * 0.5));
}`;

export function init(root, videoSelector) {
    dispose(root);

    const video = document.querySelector(videoSelector);
    const canvas = root?.querySelector('.vr-viewer-canvas');
    const projectionSelect = root?.querySelector('.vr-viewer-projection');
    const errorBox = root?.querySelector('.vr-viewer-error');
    if (!video || !canvas || !projectionSelect || !errorBox) return;

    const showError = (message) => {
        errorBox.textContent = message;
        errorBox.hidden = false;
    };

    // preserveDrawingBuffer keeps the last drawn frame readable after it is presented (a redraw only
    // happens when the video frame or the view changes), e.g. for screenshots and the E2E test.
    const gl = canvas.getContext('webgl', { alpha: false, antialias: false, preserveDrawingBuffer: true });
    const program = gl && createProgram(gl);
    if (!program) {
        showError('WebGL is not available in this browser, so the VR video cannot be unwarped.');
        return;
    }
    gl.useProgram(program);

    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const position = gl.getAttribLocation(program, 'aPos');
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);

    const texture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);

    const uLook = gl.getUniformLocation(program, 'uLook');
    const uTanHalfFov = gl.getUniformLocation(program, 'uTanHalfFov');
    const uProjection = gl.getUniformLocation(program, 'uProjection');

    let yaw = 0;
    let pitch = 0;
    let projection = projectionSelect.value;
    let hasFrame = false;
    // Set whenever the video may have a new frame to upload. `currentTime` is no signal for that: a
    // seek moves it immediately, before the frame at the new position exists.
    let frameStale = true;
    let dirty = true;
    let stopped = false;
    let frameRequest = 0;

    const tanHalfFovY = Math.tan((VERTICAL_FOV_DEGREES * Math.PI) / 360);
    const aspect = () => (canvas.height > 0 ? canvas.width / canvas.height : 1);

    // Keep the view inside the 180° the eye covers, so a drag can't scroll off into black.
    const clampLook = () => {
        const maxYaw = Math.max(0, Math.PI / 2 - Math.atan(tanHalfFovY * aspect()));
        const maxPitch = Math.max(0, Math.PI / 2 - Math.atan(tanHalfFovY));
        yaw = Math.min(Math.max(yaw, -maxYaw), maxYaw);
        pitch = Math.min(Math.max(pitch, -maxPitch), maxPitch);
    };

    const resize = () => {
        const ratio = Math.min(window.devicePixelRatio || 1, MAX_DEVICE_PIXEL_RATIO);
        const width = Math.max(1, Math.round(canvas.clientWidth * ratio));
        const height = Math.max(1, Math.round(canvas.clientHeight * ratio));
        // Assigning width/height clears the canvas even to the same value, so only touch them on
        // a real change (ResizeObserver also fires once right after observe()).
        if (canvas.width === width && canvas.height === height) return;
        canvas.width = width;
        canvas.height = height;
        clampLook();
        dirty = true;
    };
    const resizeObserver = new ResizeObserver(resize);
    resizeObserver.observe(canvas);
    resize();

    const markFrameStale = () => { frameStale = true; };
    const canTrackFrames = typeof video.requestVideoFrameCallback === 'function';
    let videoFrameRequest = 0;
    const onVideoFrame = () => {
        frameStale = true;
        videoFrameRequest = video.requestVideoFrameCallback(onVideoFrame);
    };
    if (canTrackFrames) {
        videoFrameRequest = video.requestVideoFrameCallback(onVideoFrame);
    }
    // While paused no frames are presented, but a seek or a fresh load still produces one.
    const staleEvents = ['seeked', 'loadeddata', 'canplay'];
    staleEvents.forEach((name) => video.addEventListener(name, markFrameStale));

    const stop = () => {
        stopped = true;
        cancelAnimationFrame(frameRequest);
        if (canTrackFrames) video.cancelVideoFrameCallback(videoFrameRequest);
        staleEvents.forEach((name) => video.removeEventListener(name, markFrameStale));
    };

    const render = () => {
        if (stopped) return;
        frameRequest = requestAnimationFrame(render);

        // Without requestVideoFrameCallback, re-upload every animation frame while playing.
        if ((frameStale || (!canTrackFrames && !video.paused)) && video.readyState >= 2 && video.videoWidth > 0) {
            try {
                gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, video);
            } catch {
                stop();
                showError('This video cannot be unwarped: the browser blocked reading its frames. Switch VR 2D off to keep watching normally.');
                return;
            }
            frameStale = false;
            hasFrame = true;
            dirty = true;
        }

        if (!dirty || !hasFrame) return;
        dirty = false;
        gl.viewport(0, 0, canvas.width, canvas.height);
        gl.uniform2f(uLook, yaw, pitch);
        gl.uniform2f(uTanHalfFov, tanHalfFovY * aspect(), tanHalfFovY);
        gl.uniform1f(uProjection, projection === 'equirect' ? 1 : 0);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    };

    const onVideoError = () => {
        stop();
        showError('The video failed to load in VR 2D mode. Switch VR 2D off to keep watching normally.');
    };
    video.addEventListener('error', onVideoError);

    // Dragging on the video pans the view. It is listened for on the <video> itself, not the
    // canvas, so hovering still reveals the native controls and a plain click still toggles
    // playback; a drag's trailing click is swallowed so it doesn't also pause the video.
    let drag = null;
    let swallowClick = false;
    const onPointerDown = (event) => {
        if (event.button !== 0 || !event.isPrimary) return;
        if (event.clientY > video.getBoundingClientRect().bottom - CONTROLS_STRIP_PX) return;
        drag = { id: event.pointerId, x: event.clientX, y: event.clientY, moved: false };
    };
    const onPointerMove = (event) => {
        if (!drag || event.pointerId !== drag.id) return;
        const dx = event.clientX - drag.x;
        const dy = event.clientY - drag.y;
        if (!drag.moved) {
            if (Math.hypot(dx, dy) < DRAG_THRESHOLD_PX) return;
            drag.moved = true;
            video.setPointerCapture(event.pointerId);
        }
        // Grab-style: the scene follows the pointer, so the camera moves the opposite way.
        const radiansPerPixel = (2 * Math.atan(tanHalfFovY)) / Math.max(canvas.clientHeight, 1);
        yaw -= dx * radiansPerPixel;
        pitch += dy * radiansPerPixel;
        clampLook();
        dirty = true;
        drag.x = event.clientX;
        drag.y = event.clientY;
    };
    const onPointerEnd = (event) => {
        if (!drag || event.pointerId !== drag.id) return;
        if (drag.moved) {
            swallowClick = true;
            setTimeout(() => { swallowClick = false; }, 0);
        }
        drag = null;
    };
    const onClickCapture = (event) => {
        if (!swallowClick) return;
        event.stopImmediatePropagation();
        event.preventDefault();
    };
    const onProjectionChange = () => {
        projection = projectionSelect.value;
        dirty = true;
    };

    // The player column includes the area plus the optional scrub bar, so fullscreening it keeps
    // all player controls visible. The viewer is rendered inside .video-player-area.
    const container = root.closest('.cleanup-player-column') || root.parentElement || video.parentElement || video;
    const isFullscreen = () => document.fullscreenElement === container;

    const toggleFullscreen = () => {
        if (isFullscreen()) {
            if (document.exitFullscreen) {
                document.exitFullscreen().catch(() => {});
            }
        } else {
            const req = container.requestFullscreen || container.webkitRequestFullscreen;
            if (req) {
                req.call(container).catch(() => {});
            }
        }
    };

    const onDblClick = (event) => {
        if (event.clientY > video.getBoundingClientRect().bottom - CONTROLS_STRIP_PX) return;
        toggleFullscreen();
    };
    video.addEventListener('dblclick', onDblClick);

    const onKeyDown = (event) => {
        if (event.key === 'f' || event.key === 'F') {
            if (['INPUT', 'SELECT', 'TEXTAREA'].includes(document.activeElement?.tagName)) return;
            event.preventDefault();
            toggleFullscreen();
        }
    };
    window.addEventListener('keydown', onKeyDown);

    const originalRequestFullscreen = video.requestFullscreen;
    video.requestFullscreen = function(options) {
        const req = container.requestFullscreen || container.webkitRequestFullscreen;
        return req ? req.call(container, options) : originalRequestFullscreen.call(video, options);
    };
    const originalWebkitRequestFullscreen = video.webkitRequestFullscreen;
    if (typeof video.webkitRequestFullscreen === 'function') {
        video.webkitRequestFullscreen = function(options) {
            const req = container.requestFullscreen || container.webkitRequestFullscreen;
            return req ? req.call(container, options) : originalWebkitRequestFullscreen.call(video, options);
        };
    }

    video.addEventListener('click', onClickCapture, true);
    video.addEventListener('pointerdown', onPointerDown);
    video.addEventListener('pointermove', onPointerMove);
    video.addEventListener('pointerup', onPointerEnd);
    video.addEventListener('pointercancel', onPointerEnd);
    projectionSelect.addEventListener('change', onProjectionChange);

    frameRequest = requestAnimationFrame(render);

    instances.set(root, () => {
        stop();
        resizeObserver.disconnect();
        video.removeEventListener('error', onVideoError);
        video.removeEventListener('click', onClickCapture, true);
        video.removeEventListener('pointerdown', onPointerDown);
        video.removeEventListener('pointermove', onPointerMove);
        video.removeEventListener('pointerup', onPointerEnd);
        video.removeEventListener('pointercancel', onPointerEnd);
        video.removeEventListener('dblclick', onDblClick);
        window.removeEventListener('keydown', onKeyDown);
        video.requestFullscreen = originalRequestFullscreen;
        if (originalWebkitRequestFullscreen) {
            video.webkitRequestFullscreen = originalWebkitRequestFullscreen;
        }
        if (isFullscreen() && document.exitFullscreen) {
            document.exitFullscreen().catch(() => {});
        }
        projectionSelect.removeEventListener('change', onProjectionChange);
        gl.getExtension('WEBGL_lose_context')?.loseContext();
    });
}

export function dispose(root) {
    const teardown = root && instances.get(root);
    if (teardown) {
        teardown();
        instances.delete(root);
    }
}

function createProgram(gl) {
    const compile = (type, source) => {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        return gl.getShaderParameter(shader, gl.COMPILE_STATUS) ? shader : null;
    };
    const vertex = compile(gl.VERTEX_SHADER, VERTEX_SHADER);
    const fragment = compile(gl.FRAGMENT_SHADER, FRAGMENT_SHADER);
    if (!vertex || !fragment) return null;

    const program = gl.createProgram();
    gl.attachShader(program, vertex);
    gl.attachShader(program, fragment);
    gl.linkProgram(program);
    return gl.getProgramParameter(program, gl.LINK_STATUS) ? program : null;
}
