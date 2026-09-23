"""生成精致版 中国围棋 图标。

输出：
- app.ico - 桌面快捷方式图标（多尺寸）
- logo-clearbg.png - 512x512 透明背景版（Steam logo）
- app-main.png - 256x256 主版本

设计：徽章风格
- 圆形深色木纹背景（径向渐变）
- 9x9 细棋盘线
- 中心黑子 + 左下/右上两颗白子
- 金色细边框
- 底部"中国围棋" 4 个字
"""
import os
import math
from PIL import Image, ImageDraw, ImageFont

OUT_DIR = r"C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype\installer"

# ============== 颜色 ==============
WOOD_INNER = (135, 75, 40)
WOOD_OUTER = (38, 18, 8)
GOLD = (210, 170, 90)
GOLD_DARK = (150, 110, 50)
BLACK_INNER = (60, 60, 60)
BLACK_OUTER = (8, 8, 8)
WHITE_INNER = (250, 245, 235)
WHITE_OUTER = (190, 180, 165)
GRID_COLOR = (220, 200, 140)


def radial_gradient_circle_pil(size, inner_color, outer_color, radius_pad=4):
    """生成圆形径向渐变（圆外全透明），纯 PIL 像素操作。"""
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    pixels = img.load()
    cx, cy = size / 2, size / 2
    max_dist = size / 2 - radius_pad

    for y in range(size):
        for x in range(size):
            dx = x - cx
            dy = y - cy
            dist = math.sqrt(dx * dx + dy * dy)
            if dist <= max_dist:
                t = dist / max_dist
                # 抗锯齿边缘
                if dist > max_dist - 1:
                    alpha = int(255 * (max_dist - dist))
                else:
                    alpha = 255
                r = int(inner_color[0] * (1 - t) + outer_color[0] * t)
                g = int(inner_color[1] * (1 - t) + outer_color[1] * t)
                b = int(inner_color[2] * (1 - t) + outer_color[2] * t)
                pixels[x, y] = (r, g, b, alpha)
    return img


def draw_stone(pil_img, cx, cy, r, is_black):
    """绘制棋子（径向渐变 + 高光 + 阴影）。"""
    w, h = pil_img.size
    overlay = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay, 'RGBA')

    # 阴影
    shadow_offset = max(1.5, r * 0.12)
    shadow_r = r * 1.05
    for i in range(10, 0, -1):
        t = i / 10
        rr = shadow_r * t
        alpha = int(22 * (1 - t))
        od.ellipse([cx - rr + shadow_offset, cy - rr + shadow_offset,
                    cx + rr + shadow_offset, cy + rr + shadow_offset],
                   fill=(0, 0, 0, alpha))

    # 棋子主体：纯 PIL 像素画
    stone_img = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
    stone_pix = stone_img.load()
    if is_black:
        inner, outer = BLACK_INNER, BLACK_OUTER
    else:
        inner, outer = WHITE_INNER, WHITE_OUTER

    rr = r
    for y in range(int(cy - r - 2), int(cy + r + 2)):
        for x in range(int(cx - r - 2), int(cx + r + 2)):
            dx = x - cx
            dy = y - cy
            d = math.sqrt(dx * dx + dy * dy)
            if d <= rr:
                t = d / rr
                color_r = int(inner[0] * (1 - t) + outer[0] * t)
                color_g = int(inner[1] * (1 - t) + outer[1] * t)
                color_b = int(inner[2] * (1 - t) + outer[2] * t)
                if d > rr - 1:
                    alpha = int(255 * (rr - d))
                else:
                    alpha = 255
                stone_pix[x, y] = (color_r, color_g, color_b, alpha)

    overlay = Image.alpha_composite(overlay, stone_img)

    # 高光（左上亮斑）
    hl_r = r * 0.42
    hl_cx = cx - r * 0.32
    hl_cy = cy - r * 0.32
    hl_img = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
    hl_pix = hl_img.load()
    for y in range(int(hl_cy - hl_r - 1), int(hl_cy + hl_r + 1)):
        for x in range(int(hl_cx - hl_r - 1), int(hl_cx + hl_r + 1)):
            dx = x - hl_cx
            dy = y - hl_cy
            d = math.sqrt(dx * dx + dy * dy)
            if d <= hl_r:
                t = d / hl_r
                alpha = int(160 * (1 - t) ** 2)
                hl_pix[x, y] = (255, 255, 255, alpha)
    overlay = Image.alpha_composite(overlay, hl_img)

    # 白子边缘暗描边
    if not is_black:
        edge_img = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
        ImageDraw.Draw(edge_img, 'RGBA').ellipse(
            [cx - r, cy - r, cx + r, cy + r],
            outline=(80, 60, 40, 130), width=max(1, int(r * 0.06))
        )
        overlay = Image.alpha_composite(overlay, edge_img)

    return Image.alpha_composite(pil_img, overlay)


def draw_grid(pil_img, cx, cy, span, n=9, color=GRID_COLOR, width=1, alpha=0.32):
    overlay = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay, 'RGBA')
    step = span / (n - 1)
    x0 = cx - span / 2
    y0 = cy - span / 2
    line_color = (*color, int(255 * alpha))
    for i in range(n):
        px = x0 + i * step
        py = y0 + i * step
        draw.line([(x0, py), (x0 + span, py)], fill=line_color, width=width)
        draw.line([(px, y0), (px, y0 + span)], fill=line_color, width=width)
    return Image.alpha_composite(pil_img, overlay)


def draw_gold_border(pil_img, ring_width=3):
    overlay = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay, 'RGBA')
    w, h = pil_img.size
    cx, cy = w / 2, h / 2
    r_outer = w / 2 - 2
    draw.ellipse([cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer],
                 outline=(*GOLD, 255), width=ring_width)
    r_inner = r_outer - ring_width
    draw.ellipse([cx - r_inner, cy - r_inner, cx + r_inner, cy + r_inner],
                 outline=(*GOLD_DARK, 200), width=1)
    return Image.alpha_composite(pil_img, overlay)


def draw_text_with_outline(pil_img, text, center_xy, font_size,
                           fill=(255, 248, 230), outline=(20, 12, 5), outline_width=2):
    overlay = Image.new('RGBA', pil_img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay, 'RGBA')

    font = None
    font_paths = [
        r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\msyhbd.ttc",
        r"C:\Windows\Fonts\simhei.ttf",
        r"C:\Windows\Fonts\simsun.ttc",
        r"C:\Windows\Fonts\simfang.ttf",
    ]
    for fp in font_paths:
        if os.path.exists(fp):
            try:
                font = ImageFont.truetype(fp, font_size)
                break
            except Exception:
                continue
    if font is None:
        font = ImageFont.load_default()

    cx, cy = center_xy
    bbox = draw.textbbox((0, 0), text, font=font)
    text_w = bbox[2] - bbox[0]
    text_h = bbox[3] - bbox[1]
    x = cx - text_w / 2 - bbox[0]
    y = cy - text_h / 2 - bbox[1]

    for dx in range(-outline_width, outline_width + 1):
        for dy in range(-outline_width, outline_width + 1):
            if dx * dx + dy * dy <= outline_width * outline_width:
                draw.text((x + dx, y + dy), text, font=font, fill=(*outline, 255))
    draw.text((x, y), text, font=font, fill=(*fill, 255))

    return Image.alpha_composite(pil_img, overlay)


def make_icon(size=256):
    img = radial_gradient_circle_pil(size, WOOD_INNER, WOOD_OUTER)
    cx, cy = size / 2, size / 2

    grid_span = size * 0.50
    img = draw_grid(img, cx, cy - size * 0.04, grid_span, n=9,
                    width=max(1, size // 256), alpha=0.35)

    stone_r = size * 0.08
    img = draw_stone(img, cx, cy - size * 0.02, stone_r, is_black=True)
    img = draw_stone(img, cx - size * 0.10, cy + size * 0.10, stone_r, is_black=False)
    img = draw_stone(img, cx + size * 0.10, cy + size * 0.10, stone_r, is_black=False)

    img = draw_gold_border(img, ring_width=max(2, size // 80))

    img = draw_text_with_outline(img, "中国围棋",
                                 center_xy=(cx, cy + size * 0.30),
                                 font_size=int(size * 0.16),
                                 fill=(255, 248, 230),
                                 outline=(20, 12, 5),
                                 outline_width=max(2, size // 96))
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    img_256 = make_icon(256)
    img_256.save(os.path.join(OUT_DIR, 'app-main.png'))
    print(f"[OK] app-main.png 256x256 已生成")

    img_512 = make_icon(512)
    img_512.save(os.path.join(OUT_DIR, 'logo-clearbg.png'))
    print(f"[OK] logo-clearbg.png 512x512 已生成（Steam logo）")

    pil_256 = make_icon(256)
    ico_path = os.path.join(OUT_DIR, 'app.ico')
    pil_256.save(ico_path, format='ICO',
                 sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    size_kb = os.path.getsize(ico_path) / 1024
    print(f"[OK] app.ico 多尺寸已生成 ({size_kb:.1f} KB)")

    print("\n所有图标已生成。")


if __name__ == '__main__':
    main()