const MIN_CROP_SIZE = 40;

export class ImageCropper {
    constructor(container, img, { aspectRatio, fallbackWidth, fallbackHeight }) {
        this.container = container;
        this.img = img;
        this.fallbackWidth = fallbackWidth;
        this.fallbackHeight = fallbackHeight;

        this.aspectRatio = aspectRatio;
        this.zoom = 1.0;
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

        const scale = Math.min(cWidth / nWidth, cHeight / nHeight);
        const dispW = nWidth * scale;
        const dispH = nHeight * scale;

        this.baseWidth = dispW;
        this.baseHeight = dispH;
        this.baseX = (cWidth - dispW) / 2;
        this.baseY = (cHeight - dispH) / 2;

        this.applyZoom(1.0);
        this.resetCrop();
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

    clampCropBoxToImage() {
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

    updateImgDom() {
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
        const srcX = rect.x * this.img.naturalWidth;
        const srcY = rect.y * this.img.naturalHeight;
        const srcW = rect.width * this.img.naturalWidth;
        const srcH = rect.height * this.img.naturalHeight;

        const ctx = canvas.getContext('2d');
        const cw = canvas.width;
        const ch = canvas.height;
        ctx.clearRect(0, 0, cw, ch);
        ctx.save();
        clip?.(ctx, cw, ch);
        ctx.drawImage(this.img, srcX, srcY, srcW, srcH, 0, 0, cw, ch);
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
            const buffer = await streamRef.arrayBuffer();
            activeObjectUrl = URL.createObjectURL(new Blob([buffer], { type: contentType }));
            img.src = activeObjectUrl;
        },
        initCropper(container, img, ...previewCanvases) {
            destroyActiveCropper();
            activeCropper = createCropper(container, img, ...previewCanvases);
            activeCropper.init();
            return true;
        },
        setAspectRatio: (ratio) => activeCropper?.setAspectRatio(ratio),
        setZoom: (zoom) => activeCropper?.applyZoom(zoom),
        resetCrop: () => activeCropper?.resetCrop(),
        getCropRect: () => activeCropper?.getCropRect() ?? { x: 0, y: 0, width: 1, height: 1 },
        destroyCropper() {
            destroyActiveCropper();
            revokeImageSource();
        }
    };
}
