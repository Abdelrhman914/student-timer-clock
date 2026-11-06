#!/usr/bin/env python3
"""Crops the raw screenshots in screenshots/ to the app windows and puts them on a uniform desktop background."""
from PIL import Image, ImageDraw, ImageFilter
import os
SRC, DST = "screenshots", "docs/images"
BG = (58, 110, 165)          # Windows-style desktop blue
MARGIN = 16
names = {
    "01-main-grid-and-widgets.png": "main-window.png",
    "02-widget-style-dialog.png": "widget-style.png",
    "03-dial-layout-preview.png": "widget-dial.png",
    "04-action-dialog.png": "action-dialog.png",
    "05-shutdown-countdown.png": "action-test-countdown.png",
    "06-board-view.png": "board.png",
    "07-chained-timers.png": "chained-timers.png",
    "08-when-done-tab.png": "when-done-tab.png",
    "09-installer-terms.png": "installer-terms.png",
    "10-installer-components.png": "installer-components.png",
    "11-stats-lap-titles-totals.png": "stats-lap-titles.png",
    "12-stats-sessions-tab.png": "stats-sessions.png",
    "13-stats-group-by-type.png": "stats-by-type.png",
    "14-stopwatch-lap-summary.png": "stopwatches.png",
    "15-settings-backup.png": "settings-backup.png",
    "16-restore-confirm.png": "restore.png",
    "17-help-security.png": "help-security.png",
    "18-integrity-violation.png": "integrity-alert.png",
    "19-installer-finish.png": "installer-finish.png",
}
for src, dst in names.items():
    im = Image.open(os.path.join(SRC, src)).convert("RGB")
    w, h = im.size
    if (w, h) == (1400, 900):
        # stray task-bar strip of the headless X server in the top-left corner
        ImageDraw.Draw(im).rectangle([0, 0, 170, 55], fill=im.getpixel((300, 880)))
        # make the background (connected to the border) uniform blue
        for seed in [(1, 1), (w - 2, h - 2), (1, h - 2), (w - 2, 1), (700, 895), (5, 450)]:
            if im.getpixel(seed) in [(0, 0, 0), BG]:
                ImageDraw.floodfill(im, seed, (1, 2, 3), thresh=0)
        px = im.load()
        mask = Image.new("L", im.size, 0); mp = mask.load()
        for y in range(h):
            for x in range(w):
                if px[x, y] != (1, 2, 3): mp[x, y] = 255
        bbox = mask.getbbox()
        im = im.crop(bbox); mask = mask.crop(bbox)
        out = Image.new("RGB", (im.width + 2 * MARGIN, im.height + 2 * MARGIN), BG)
        # soft shadow
        sh = Image.new("L", out.size, 0); sh.paste(mask, (MARGIN + 3, MARGIN + 4)); sh = sh.filter(ImageFilter.GaussianBlur(5))
        out.paste(Image.new("RGB", out.size, (30, 55, 85)), (0, 0), sh.point(lambda v: v * 0.55))
        out.paste(im, (MARGIN, MARGIN), mask)
    else:
        out = Image.new("RGB", (w + 2 * MARGIN, h + 2 * MARGIN), BG); out.paste(im, (MARGIN, MARGIN))
    out.save(os.path.join(DST, dst), optimize=True)
    print(f"{dst:28s} {out.size}")
