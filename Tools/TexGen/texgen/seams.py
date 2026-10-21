"""Seam check. The wrap-around seam of a tileable image is just another pair of neighbouring columns/rows, so its
gradient (mean absolute difference across the seam, in X and Y) must look like the strongest ordinary neighbour
gradients of the image. The score is that seam gradient divided by (about) the 95th percentile of the gradients of all other
column/row pairs: about 1 or below for a tileable texture (even when a joint or edge sits on the border), far above
for one with a visible seam. The plan's first idea (divide by the mean gradient) misfired on brick and tile, whose
joints start exactly at the tile border."""
import numpy as np

PASS_BELOW = 1.5


def seam_score(img):
    img = np.asarray(img, dtype=np.float64)
    if img.ndim == 3:
        img = img.mean(axis=-1)
    # gx[x] = how much column x differs from column x-1; gx[0] compares the first column with the last (the seam).
    gx = np.abs(img - np.roll(img, 1, axis=1)).mean(axis=0)
    gy = np.abs(img - np.roll(img, 1, axis=0)).mean(axis=1)
    return float(max(gx[0] / _reference(gx[1:]), gy[0] / _reference(gy[1:])))


def _reference(others):
    # The 95th percentile, but never less than 3/4 of the strongest neighbour gradient: a few sparse joints or edges
    # (under 5 % of the columns) would otherwise leave the percentile on the flat background.
    return max(float(np.percentile(others, 95)), 0.75 * float(others.max()), 1e-9)


def passes(score):
    return score < PASS_BELOW
