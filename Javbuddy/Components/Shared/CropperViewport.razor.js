const MIN_CROP_SIZE = 40;
// Keeps a crop box this many display pixels inside a rotated image's edge, so the antialiased
// border of a rotated image never ends up in the saved crop.
const ROTATED_EDGE_INSET = 2;

/** Natural size of the smallest axis-aligned frame holding a width x height image rotated by `rad`. */
export function rotatedFrameSize(width, height, rad) {
    const c = Math.abs(Math.cos(rad));
    const s = Math.abs(Math.sin(rad));
    return { width: width * c + height * s, height: width * s + height * c };
}

/**
 * Shrinks (about its centre, keeping its aspect ratio) and slides `box` so that it lies inside the
 * rectangle of half-size (halfW, halfH) centred on `center` and rotated clockwise by `rad`.
 * Works in the rectangle's own axes, where containment is two independent range checks.
 */
export function clampBoxInRotatedRect(box, center, halfW, halfH, rad) {
    const c = Math.cos(rad);
    const s = Math.sin(rad);
    const ac = Math.abs(c);
    const as = Math.abs(s);
    const a = Math.max(1, halfW - ROTATED_EDGE_INSET);
    const b = Math.max(1, halfH - ROTATED_EDGE_INSET);

    // Half-extent of the box along each of the rectangle's axes, per unit of box size.
    const needU = (box.width * ac + box.height * as) / 2;
    const needV = (box.width * as + box.height * ac) / 2;
    const f = Math.min(1, needU > 0 ? a / needU : 1, needV > 0 ? b / needV : 1);
    const width = box.width * f;
    const height = box.height * f;

    const limU = Math.max(0, a - (width * ac + height * as) / 2);
    const limV = Math.max(0, b - (width * as + height * ac) / 2);

    const dx = box.x + box.width / 2 - center.x;
    const dy = box.y + box.height / 2 - center.y;
    const u = Math.max(-limU, Math.min(limU, dx * c + dy * s));
    const v = Math.max(-limV, Math.min(limV, -dx * s + dy * c));
    const cx = center.x + u * c - v * s;
    const cy = center.y + u * s + v * c;

    return { x: cx - width / 2, y: cy - height / 2, width, height };
}

export class ImageCropper {
    constructor(container, img, { aspectRatio, fallbackWidth, fallbackHeight }) {
        this.container = container;
        this.img = img;
        this.fallbackWidth = fallbackWidth;
        this.fallbackHeight = fallbackHeight;

        this.aspectRatio = aspectRatio;
        this.zoom = 1.0;
        this.rotation = 0; // radians, clockwise; the image is straightened inside an axis-aligned frame
        this.minZoom = 1.0;
        this.maxZoom = 3.0;

        this.imgPos = { x: 0, y: 0, width: 0, height: 0 };
        this.cropBox = { x: 0, y: 0, width: 0, height: 0 };

        this.isDragging = false;
        this.isResizing = false;
        this.resizeHandle = null;
        this.dragStart = { x: 0, y: 0 };
        this.cropStart = { x: 0, y: 0, width: 0, height: 0 };

        this.cropBoxEl = container.querySelector('.cropper-crop-box');
    }

    /** Subclasses return the crop box shown on load and after a reset. */
    initialCropBox() {
        throw new Error('initialCropBox must be overridden');
    }

    /** Subclasses draw their preview canvases from getCropRect(). */
    updatePreviews() {}

    init() {
        if (!this.img.complete) {
            this.img.onload = () => this.setupGeometry();
            // An undecodable image would otherwise stay hidden (see data-ready) with no explanation.
            this.img.onerror = () => {
                this.container.dataset.ready = '1';
                this.onImageError?.();
            };
        } else {
            this.setupGeometry();
        }

        this.bindEvents();
    }

    setupGeometry() {
        const cWidth = this.container.clientWidth || this.fallbackWidth;
        const cHeight = this.container.clientHeight || this.fallbackHeight;
        const nWidth = this.img.naturalWidth || this.fallbackWidth;
        const nHeight = this.img.naturalHeight || this.fallbackHeight;

        // With a rotation, imgPos is the frame holding the rotated image, and crop rects are
        // relative to that frame.
        const frame = this.rotation === 0
            ? { width: nWidth, height: nHeight }
            : rotatedFrameSize(nWidth, nHeight, this.rotation);
        this.frameNaturalWidth = frame.width;
        this.frameNaturalHeight = frame.height;

        const scale = Math.min(cWidth / frame.width, cHeight / frame.height);
        const dispW = frame.width * scale;
        const dispH = frame.height * scale;

        this.baseWidth = dispW;
        this.baseHeight = dispH;
        this.baseX = (cWidth - dispW) / 2;
        this.baseY = (cHeight - dispH) / 2;

        this.applyZoom(1.0);
        this.resetCrop();
        // Everything stays hidden until the first layout, so no empty/broken <img> or stray crop box shows.
        this.container.dataset.ready = '1';
    }

    applyZoom(newZoom) {
        this.zoom = Math.max(this.minZoom, Math.min(this.maxZoom, newZoom));
        const cWidth = this.container.clientWidth;
        const cHeight = this.container.clientHeight;

        const currentCenterX = this.cropBox.width > 0 ? this.cropBox.x + this.cropBox.width / 2 : cWidth / 2;
        const currentCenterY = this.cropBox.height > 0 ? this.cropBox.y + this.cropBox.height / 2 : cHeight / 2;

        const w = this.baseWidth * this.zoom;
        const h = this.baseHeight * this.zoom;
        const x = currentCenterX - (currentCenterX - this.imgPos.x) * (w / (this.imgPos.width || w));
        const y = currentCenterY - (currentCenterY - this.imgPos.y) * (h / (this.imgPos.height || h));

        this.imgPos = {
            width: w,
            height: h,
            x: Math.min(Math.max(x, cWidth - w), 0),
            y: Math.min(Math.max(y, cHeight - h), 0)
        };

        if (w <= cWidth) this.imgPos.x = (cWidth - w) / 2;
        if (h <= cHeight) this.imgPos.y = (cHeight - h) / 2;

        this.updateImgDom();
        this.clampCropBoxToImage();
        this.updateCropDom();
        this.updatePreviews();
    }

    resetCrop() {
        this.cropBox = this.initialCropBox();
        this.clampCropBoxToImage();
        this.updateCropDom();
        this.updatePreviews();
    }

    setAspectRatio(ratio) {
        this.aspectRatio = ratio;
        this.resetCrop();
    }

    /** Straightens the image by `degrees` (clockwise) and lays the view out again; the zoom level is kept. */
    setRotation(degrees) {
        this.rotation = (Number(degrees) || 0) * Math.PI / 180;
        if (this.img.complete && this.img.naturalWidth) {
            const zoom = this.zoom;
            // Keep the user's crop box: remember its centre relative to the frame, lay out again (which
            // resets it), then put it back, clamped to the newly straightened image. The frame's shape
            // changes with the angle, so the box is resized by one factor (the image's own display scale)
            // rather than per axis — that keeps a locked aspect ratio exact.
            const old = this.imgPos.width > 0 && this.imgPos.height > 0 ? {
                cx: (this.cropBox.x + this.cropBox.width / 2 - this.imgPos.x) / this.imgPos.width,
                cy: (this.cropBox.y + this.cropBox.height / 2 - this.imgPos.y) / this.imgPos.height,
                width: this.cropBox.width,
                height: this.cropBox.height,
                imageScale: this.imgPos.width / this.frameNaturalWidth
            } : null;
            this.setupGeometry();
            this.applyZoom(zoom);
            if (old) {
                const factor = (this.imgPos.width / this.frameNaturalWidth) / old.imageScale;
                const width = old.width * factor;
                const height = old.height * factor;
                const rel = { cx: old.cx, cy: old.cy };
                this.cropBox = {
                    x: this.imgPos.x + rel.cx * this.imgPos.width - width / 2,
                    y: this.imgPos.y + rel.cy * this.imgPos.height - height / 2,
                    width,
                    height
                };
                this.clampCropBoxToImage();
                this.updateCropDom();
                this.updatePreviews();
            }
        }
    }

    clampCropBoxToImage() {
        if (this.rotation !== 0) {
            this.clampCropBoxToRotatedImage();
            return;
        }

        const imgRight = this.imgPos.x + this.imgPos.width;
        const imgBottom = this.imgPos.y + this.imgPos.height;

        this.cropBox.width = Math.max(MIN_CROP_SIZE, Math.min(this.cropBox.width, this.imgPos.width));
        this.cropBox.height = Math.max(MIN_CROP_SIZE, Math.min(this.cropBox.height, this.imgPos.height));

        if (this.cropBox.x < this.imgPos.x) this.cropBox.x = this.imgPos.x;
        if (this.cropBox.y < this.imgPos.y) this.cropBox.y = this.imgPos.y;

        if (this.cropBox.x + this.cropBox.width > imgRight) {
            this.cropBox.x = imgRight - this.cropBox.width;
        }
        if (this.cropBox.y + this.cropBox.height > imgBottom) {
            this.cropBox.y = imgBottom - this.cropBox.height;
        }
    }

    clampCropBoxToRotatedImage() {
        const scale = this.imgPos.width / this.frameNaturalWidth;
        const nWidth = this.img.naturalWidth || this.fallbackWidth;
        const nHeight = this.img.naturalHeight || this.fallbackHeight;
        this.cropBox = clampBoxInRotatedRect(
            this.cropBox,
            { x: this.imgPos.x + this.imgPos.width / 2, y: this.imgPos.y + this.imgPos.height / 2 },
            nWidth * scale / 2,
            nHeight * scale / 2,
            this.rotation);
    }

    updateImgDom() {
        if (this.rotation !== 0) {
            const scale = this.imgPos.width / this.frameNaturalWidth;
            const w = (this.img.naturalWidth || this.fallbackWidth) * scale;
            const h = (this.img.naturalHeight || this.fallbackHeight) * scale;
            this.img.style.width = `${w}px`;
            this.img.style.height = `${h}px`;
            this.img.style.left = `${this.imgPos.x + (this.imgPos.width - w) / 2}px`;
            this.img.style.top = `${this.imgPos.y + (this.imgPos.height - h) / 2}px`;
            this.img.style.transform = `rotate(${this.rotation}rad)`;
            return;
        }

        this.img.style.transform = '';
        this.img.style.width = `${this.imgPos.width}px`;
        this.img.style.height = `${this.imgPos.height}px`;
        this.img.style.left = `${this.imgPos.x}px`;
        this.img.style.top = `${this.imgPos.y}px`;
    }

    updateCropDom() {
        if (!this.cropBoxEl) return;
        this.cropBoxEl.style.left = `${this.cropBox.x}px`;
        this.cropBoxEl.style.top = `${this.cropBox.y}px`;
        this.cropBoxEl.style.width = `${this.cropBox.width}px`;
        this.cropBoxEl.style.height = `${this.cropBox.height}px`;
    }

    /** Draws the current crop region of the image into a canvas, optionally clipped by `clip`. */
    drawPreview(canvas, clip) {
        if (!canvas || !this.img.naturalWidth || !this.img.naturalHeight) return;

        const rect = this.getCropRect();
        const rotated = this.rotation !== 0;
        const frameW = rotated ? this.frameNaturalWidth : this.img.naturalWidth;
        const frameH = rotated ? this.frameNaturalHeight : this.img.naturalHeight;
        const srcX = rect.x * frameW;
        const srcY = rect.y * frameH;
        const srcW = rect.width * frameW;
        const srcH = rect.height * frameH;

        const ctx = canvas.getContext('2d');
        const cw = canvas.width;
        const ch = canvas.height;
        ctx.clearRect(0, 0, cw, ch);
        ctx.save();
        clip?.(ctx, cw, ch);
        if (rotated) {
            // Draw the rotated image into the frame, then the crop region of the frame onto the canvas.
            ctx.scale(cw / srcW, ch / srcH);
            ctx.translate(-srcX, -srcY);
            ctx.translate(frameW / 2, frameH / 2);
            ctx.rotate(this.rotation);
            ctx.drawImage(this.img, -this.img.naturalWidth / 2, -this.img.naturalHeight / 2);
        } else {
            ctx.drawImage(this.img, srcX, srcY, srcW, srcH, 0, 0, cw, ch);
        }
        ctx.restore();
    }

    bindEvents() {
        this.onPointerDown = (e) => this.handlePointerDown(e);
        this.onPointerMove = (e) => this.handlePointerMove(e);
        this.onPointerUp = () => this.handlePointerUp();
        this.onWheel = (e) => this.handleWheel(e);

        this.container.addEventListener('pointerdown', this.onPointerDown);
        window.addEventListener('pointermove', this.onPointerMove);
        window.addEventListener('pointerup', this.onPointerUp);
        this.container.addEventListener('wheel', this.onWheel, { passive: false });
    }

    handleWheel(e) {
        e.preventDefault();
        const delta = e.deltaY < 0 ? 0.1 : -0.1;
        this.applyZoom(this.zoom + delta);
        this.onZoomChanged?.(this.zoom);
    }

    handlePointerDown(e) {
        const handle = e.target.closest('.crop-handle');
        if (handle) {
            this.isResizing = true;
            this.resizeHandle = handle.dataset.handle;
            this.dragStart = { x: e.clientX, y: e.clientY };
            this.cropStart = { ...this.cropBox };
            e.target.setPointerCapture?.(e.pointerId);
            e.stopPropagation();
            return;
        }

        const box = e.target.closest('.cropper-crop-box');
        if (box) {
            this.isDragging = true;
            this.dragStart = { x: e.clientX, y: e.clientY };
            this.cropStart = { ...this.cropBox };
            box.setPointerCapture?.(e.pointerId);
            e.stopPropagation();
        }
    }

    handlePointerMove(e) {
        if (this.isDragging) {
            const dx = e.clientX - this.dragStart.x;
            const dy = e.clientY - this.dragStart.y;

            const minX = this.imgPos.x;
            const minY = this.imgPos.y;
            const maxX = this.imgPos.x + this.imgPos.width - this.cropBox.width;
            const maxY = this.imgPos.y + this.imgPos.height - this.cropBox.height;

            this.cropBox.x = Math.max(minX, Math.min(maxX, this.cropStart.x + dx));
            this.cropBox.y = Math.max(minY, Math.min(maxY, this.cropStart.y + dy));
            if (this.rotation !== 0) this.clampCropBoxToImage();

            this.updateCropDom();
            this.updatePreviews();
            return;
        }

        if (this.isResizing) {
            const dx = e.clientX - this.dragStart.x;
            const dy = e.clientY - this.dragStart.y;
            this.applyResize(dx, dy);
            this.clampCropBoxToImage();
            this.updateCropDom();
            this.updatePreviews();
        }
    }

    applyResize(dx, dy) {
        let { x, y, width, height } = this.cropStart;
        const handle = this.resizeHandle;

        if (this.aspectRatio) {
            if (handle === 'se') {
                const delta = Math.max(dx, dy * this.aspectRatio);
                width += delta;
                height = width / this.aspectRatio;
            } else if (handle === 'nw') {
                const delta = Math.min(dx, dy * this.aspectRatio);
                x += delta;
                width -= delta;
                y += delta / this.aspectRatio;
                height -= delta / this.aspectRatio;
            } else if (handle === 'ne') {
                width += dx;
                const newH = width / this.aspectRatio;
                y -= (newH - height);
                height = newH;
            } else if (handle === 'sw') {
                x += dx;
                width -= dx;
                height = width / this.aspectRatio;
            } else if (handle === 'e' || handle === 'w') {
                const delta = handle === 'e' ? dx : -dx;
                width += delta;
                height = width / this.aspectRatio;
                if (handle === 'w') x -= delta;
            } else if (handle === 's' || handle === 'n') {
                const delta = handle === 's' ? dy : -dy;
                height += delta;
                width = height * this.aspectRatio;
                if (handle === 'n') y -= delta;
            }
        } else {
            if (handle.includes('e')) width += dx;
            if (handle.includes('s')) height += dy;
            if (handle.includes('w')) {
                x += dx;
                width -= dx;
            }
            if (handle.includes('n')) {
                y += dy;
                height -= dy;
            }
        }

        if (width >= MIN_CROP_SIZE && height >= MIN_CROP_SIZE) {
            this.cropBox = { x, y, width, height };
        }
    }

    handlePointerUp() {
        this.isDragging = false;
        this.isResizing = false;
        this.resizeHandle = null;
    }

    getCropRect() {
        if (!this.imgPos.width || !this.imgPos.height) {
            return { x: 0, y: 0, width: 1, height: 1 };
        }

        const relX = (this.cropBox.x - this.imgPos.x) / this.imgPos.width;
        const relY = (this.cropBox.y - this.imgPos.y) / this.imgPos.height;
        const relW = this.cropBox.width / this.imgPos.width;
        const relH = this.cropBox.height / this.imgPos.height;

        return {
            x: Math.max(0, Math.min(1, relX)),
            y: Math.max(0, Math.min(1, relY)),
            width: Math.max(0.001, Math.min(1, relW)),
            height: Math.max(0.001, Math.min(1, relH))
        };
    }

    destroy() {
        this.container.removeEventListener('pointerdown', this.onPointerDown);
        window.removeEventListener('pointermove', this.onPointerMove);
        window.removeEventListener('pointerup', this.onPointerUp);
        this.container.removeEventListener('wheel', this.onWheel);
    }
}

/** The JS-interop surface a crop modal imports: one live cropper and one blob URL at a time. */
export function createCropperSession(createCropper) {
    let activeCropper = null;
    let activeObjectUrl = null;
    let host = null;

    function revokeImageSource() {
        if (activeObjectUrl) {
            URL.revokeObjectURL(activeObjectUrl);
            activeObjectUrl = null;
        }
    }

    function destroyActiveCropper() {
        activeCropper?.destroy();
        activeCropper = null;
    }

    return {
        async setImageSource(img, streamRef, contentType) {
            revokeImageSource();
            delete img.parentElement.dataset.ready;
            const buffer = await streamRef.arrayBuffer();
            activeObjectUrl = URL.createObjectURL(new Blob([buffer], { type: contentType }));
            img.src = activeObjectUrl;
        },
        initCropper(container, img, ...previewCanvases) {
            destroyActiveCropper();
            activeCropper = createCropper(container, img, ...previewCanvases);
            // Wheel zoom and load failures happen in JS only; tell the .NET host (OnZoomChanged / OnImageError).
            activeCropper.onZoomChanged = (zoom) => host?.invokeMethodAsync('OnZoomChanged', zoom).catch(() => {});
            activeCropper.onImageError = () => host?.invokeMethodAsync('OnImageError').catch(() => {});
            activeCropper.init();
            return true;
        },
        setHost(dotNetRef) { host = dotNetRef; },
        setAspectRatio: (ratio) => activeCropper?.setAspectRatio(ratio),
        setZoom: (zoom) => activeCropper?.applyZoom(zoom),
        setRotation: (degrees) => activeCropper?.setRotation(degrees),
        resetCrop: () => activeCropper?.resetCrop(),
        getCropRect: () => activeCropper?.getCropRect() ?? { x: 0, y: 0, width: 1, height: 1 },
        destroyCropper() {
            destroyActiveCropper();
            revokeImageSource();
        }
    };
}
