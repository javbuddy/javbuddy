import { ImageCropper, createCropperSession } from './CropperViewport.razor.js';

class ActorCropper extends ImageCropper {
    constructor(container, img, previewCircleCanvas, previewSquareCanvas) {
        super(container, img, { aspectRatio: 1.0, fallbackWidth: 500, fallbackHeight: 380 });
        this.previewCircleCanvas = previewCircleCanvas;
        this.previewSquareCanvas = previewSquareCanvas;
    }

    initialCropBox() {
        const availW = Math.min(this.imgPos.width, this.container.clientWidth);
        const availH = Math.min(this.imgPos.height, this.container.clientHeight);

        let cropW, cropH;
        if (this.aspectRatio) {
            if (availW / availH > this.aspectRatio) {
                cropH = availH * 0.85;
                cropW = cropH * this.aspectRatio;
            } else {
                cropW = availW * 0.85;
                cropH = cropW / this.aspectRatio;
            }
        } else {
            cropW = availW * 0.85;
            cropH = availH * 0.85;
        }

        return {
            x: Math.max(this.imgPos.x, this.imgPos.x + (this.imgPos.width - cropW) / 2),
            y: Math.max(this.imgPos.y, this.imgPos.y + (this.imgPos.height - cropH) / 2),
            width: cropW,
            height: cropH
        };
    }

    updatePreviews() {
        this.drawPreview(this.previewCircleCanvas, (ctx, cw, ch) => {
            ctx.beginPath();
            ctx.arc(cw / 2, ch / 2, cw / 2, 0, Math.PI * 2);
            ctx.closePath();
            ctx.clip();
        });
        this.drawPreview(this.previewSquareCanvas);
    }
}

export const { setImageSource, initCropper, setAspectRatio, setZoom, resetCrop, getCropRect, destroyCropper } =
    createCropperSession((container, img, circleCanvas, squareCanvas) => new ActorCropper(container, img, circleCanvas, squareCanvas));
