import { ImageCropper, createCropperSession } from './CropperViewport.razor.js';

// Default to the DVD front-case ratio (1:1.42).
const DVD_COVER_ASPECT = 1.0 / 1.42;
// A wrap-around jacket scan is landscape; its front cover is the right ~47.2% (RightCropDivisor = 1.895734597).
const JACKET_MIN_ASPECT = 1.2;
const JACKET_FRONT_RATIO = 1.0 / 1.895734597;

class MovieCoverCropper extends ImageCropper {
    constructor(container, img, previewPosterCanvas) {
        super(container, img, { aspectRatio: DVD_COVER_ASPECT, fallbackWidth: 560, fallbackHeight: 420 });
        this.previewPosterCanvas = previewPosterCanvas;
    }

    initialCropBox() {
        const naturalAspect = (this.img.naturalWidth && this.img.naturalHeight)
            ? (this.img.naturalWidth / this.img.naturalHeight)
            : 1.5;
        const { x, y, width, height } = this.imgPos;

        if (naturalAspect > JACKET_MIN_ASPECT) {
            let cropW = width * JACKET_FRONT_RATIO;
            let cropH = height;
            let cropX = x + width - cropW;
            let cropY = y;

            if (this.aspectRatio) {
                cropW = Math.min(cropW, cropH * this.aspectRatio);
                cropH = cropW / this.aspectRatio;
                cropX = x + width - cropW;
                cropY = y + (height - cropH) / 2;
            }
            return { x: cropX, y: cropY, width: cropW, height: cropH };
        }

        let cropW, cropH;
        if (this.aspectRatio) {
            if (width / height > this.aspectRatio) {
                cropH = height * 0.9;
                cropW = cropH * this.aspectRatio;
            } else {
                cropW = width * 0.9;
                cropH = cropW / this.aspectRatio;
            }
        } else {
            cropW = width * 0.85;
            cropH = height * 0.85;
        }
        return { x: x + (width - cropW) / 2, y: y + (height - cropH) / 2, width: cropW, height: cropH };
    }

    updatePreviews() {
        this.drawPreview(this.previewPosterCanvas);
    }
}

export const { setImageSource, initCropper, setAspectRatio, setZoom, resetCrop, getCropRect, destroyCropper } =
    createCropperSession((container, img, previewPosterCanvas) => new MovieCoverCropper(container, img, previewPosterCanvas));
