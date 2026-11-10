import { describe, expect, it } from 'vitest';
import { clampView, fitScale, MAX_PREVIEW_ZOOM, zoomAt } from '../src/shell/PicturePreview.tsx';

describe('picture preview geometry (PRD §10.6)', () => {
  it('fits a large picture to the view and never enlarges a small one', () => {
    expect(fitScale(2000, 1000, 1000, 800)).toBe(0.5);
    expect(fitScale(200, 100, 1000, 800)).toBe(1);
  });

  it('centres a picture smaller than the view, and keeps a larger one’s edges outside it', () => {
    expect(clampView({ scale: 1, x: 50, y: -30 }, 200, 100, 1000, 800)).toEqual({ scale: 1, x: 0, y: 0 });
    expect(clampView({ scale: 2, x: 5000, y: -5000 }, 1000, 1000, 1000, 800)).toEqual({
      scale: 2,
      x: 500,
      y: -600,
    });
  });

  it('zooms about the pointer, between the fit scale and 800%', () => {
    // The picture point under the pointer stays put: (100 - 0) / 1 = (100 - x) / 2 → x = -100.
    expect(zoomAt({ scale: 1, x: 0, y: 0 }, 2, 100, 0, 1)).toEqual({ scale: 2, x: -100, y: 0 });
    expect(zoomAt({ scale: 1, x: 0, y: 0 }, 100, 0, 0, 1).scale).toBe(MAX_PREVIEW_ZOOM);
    expect(zoomAt({ scale: 1, x: 0, y: 0 }, 0.1, 0, 0, 0.5).scale).toBe(0.5);
  });
});
