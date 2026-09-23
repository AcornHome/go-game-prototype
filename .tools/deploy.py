"""
一键重新部署 中国围棋 桌面版。

用途：本机 `dotnet build` 之后运行本脚本，它会
  1) 把 desktop/bin/Debug/net10.0-windows/ 完整同步到 dist/（exe + dll + 依赖）
  2) 重建桌面快捷方式「中国围棋.lnk」，指向 dist/GoGame.exe
  3) 强制使用 installer/app.ico 作为图标（避免 .NET apphost 无图标）
  4) 清理旧版本名残留（GoSmart · 人机围棋 / 人机围棋 / GoGame 等 .lnk）
  5) 自检 .lnk 结构

为什么必须整目录同步：
  GoGame.exe 只是 .NET apphost（约 160KB），运行时依赖同目录的 GoGame.dll
  （约 250KB）以及 deps.json / runtimeconfig.json。只复制 exe 到桌面会导致
  双击无反应——这就是之前「桌面图标点不开」的根因。

用法（PowerShell 或 CMD）：
  python .tools\\deploy.py

注意：
  dist/KataGo/ 是随游戏分发的引擎目录（约 108MB），不是构建产物，
  因此每次部署只清理 GoGame.* 构建文件，KataGo 目录保留并增量同步。
"""
import ctypes
import os
import shutil
import struct
import sys
from ctypes import c_void_p, c_wchar_p, c_uint

# ---------------- stdout/stderr UTF-8 强制开启 ----------------
# v1.4.1 修复：cmd 默认 GBK，输出 ✓/✗ 或其他 Unicode 会 UnicodeEncodeError
# 导致 deploy.py 抛异常退出、构建脚本失败。
# 把 stdout/stderr 强制重配为 UTF-8（带 errors=replace 兜底），
# 这样无论输出到控制台还是重定向到 .log 文件都不会崩。
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")  # type: ignore[attr-defined]
    except (AttributeError, ValueError):
        # 旧版本 Python 没有 reconfigure，跳过（构建脚本会用 Python 3.7+）
        pass
del _stream

# ---------------- 路径配置 ----------------
REPO = r"C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype"
BIN_DIR = os.path.join(REPO, "desktop", "bin", "Release", "net10.0-windows")
DIST_DIR = os.path.join(REPO, "dist")
LINK_NAME = "中国围棋"
DESCRIPTION = "中国围棋（KataGo 人机对弈）"

EXE_NAME = "GoGame.exe"

# 桌面 .lnk 强制使用的图标（避免 .NET apphost 无图标导致回退到默认 exe 图标）
ICON_PATH = os.path.join(REPO, "installer", "app.ico")

# KataGo 引擎源目录（开发机）-> dist 内的分发目录名
KATAGO_SRC = r"C:\Tools\KataGo"
KATAGO_DST_NAME = "KataGo"

# 分发时不需要带上的文件
KATAGO_SKIP_DIRS = {"gtp_logs"}
KATAGO_SKIP_FILES = {
    "analysis_example.cfg",
    "contribute_example.cfg",
    "gtp_human5k_example.cfg",
    "gtp_human9d_search_example.cfg",
    "match_example.cfg",
}

# 合规材料源目录（MIT 许可证全文 + 第三方声明）
LICENSES_SRC = os.path.join(REPO, "dist-licenses")

shell32 = ctypes.windll.shell32
shell32.ILCreateFromPathW.argtypes = [c_wchar_p]
shell32.ILCreateFromPathW.restype = ctypes.c_void_p
shell32.ILGetSize.argtypes = [ctypes.c_void_p]
shell32.ILGetSize.restype = c_uint


# ---------------- 步骤 1：同步 bin -> dist ----------------
def sync_dist():
    if not os.path.isdir(BIN_DIR):
        print(f"[!] 找不到编译输出目录：{BIN_DIR}")
        print("    请先执行：cd desktop && dotnet build")
        return False

    exe = os.path.join(BIN_DIR, EXE_NAME)
    if not os.path.exists(exe):
        print(f"[!] 找不到 {EXE_NAME}，请先 dotnet build")
        return False

    # 只清理上一次的构建产物；KataGo 等资源目录必须保留
    # （早期版本直接 rmtree 整个 dist，会把 108MB 的引擎目录一起删掉）
    if os.path.isdir(DIST_DIR):
        for name in os.listdir(DIST_DIR):
            if name.startswith("GoGame."):
                p = os.path.join(DIST_DIR, name)
                if os.path.isfile(p):
                    os.remove(p)
    else:
        os.makedirs(DIST_DIR, exist_ok=True)

    for name in os.listdir(BIN_DIR):
        src = os.path.join(BIN_DIR, name)
        if os.path.isfile(src):
            shutil.copy2(src, os.path.join(DIST_DIR, name))

    files = sorted(f for f in os.listdir(DIST_DIR)
                   if os.path.isfile(os.path.join(DIST_DIR, f)))
    print(f"[1] 已同步 {len(files)} 个文件 -> {DIST_DIR}")
    for f in files:
        size = os.path.getsize(os.path.join(DIST_DIR, f))
        print(f"      {f:<32} {size:>9,} bytes")
    return True


# ---------------- 步骤 1b：增量同步 KataGo 引擎 ----------------
def sync_katago():
    """把开发机的 KataGo 同步到 dist/KataGo/。

    权重文件 94MB，所以按 大小+修改时间 做增量判断，避免每次全量复制。
    """
    if not os.path.isdir(KATAGO_SRC):
        print(f"[!] 找不到 KataGo 源目录：{KATAGO_SRC}")
        print("    游戏将无法启动 AI，跳过该步")
        return False

    dst = os.path.join(DIST_DIR, KATAGO_DST_NAME)
    os.makedirs(dst, exist_ok=True)

    updated = 0
    skipped = 0
    for root, dirs, files in os.walk(KATAGO_SRC):
        rel = os.path.relpath(root, KATAGO_SRC)
        dirs[:] = [d for d in dirs if d not in KATAGO_SKIP_DIRS]

        out_dir = dst if rel == "." else os.path.join(dst, rel)
        os.makedirs(out_dir, exist_ok=True)

        for f in files:
            if rel == "." and f in KATAGO_SKIP_FILES:
                continue
            src_file = os.path.join(root, f)
            dst_file = os.path.join(out_dir, f)

            if os.path.exists(dst_file):
                same_size = os.path.getsize(dst_file) == os.path.getsize(src_file)
                same_time = abs(os.path.getmtime(dst_file) -
                                os.path.getmtime(src_file)) < 2
                if same_size and same_time:
                    skipped += 1
                    continue

            shutil.copy2(src_file, dst_file)
            updated += 1

    total = sum(os.path.getsize(os.path.join(r, f))
                for r, _, fs in os.walk(dst) for f in fs)
    print(f"[1b] KataGo 引擎目录已就绪 -> {dst}")
    print(f"      更新 {updated} 个文件，跳过 {skipped} 个（未变化）")
    print(f"      目录总大小 {total / 1024 / 1024:.1f} MB")
    return True


# ---------------- 步骤 1c：同步合规材料 ----------------
def sync_licenses():
    """把 dist-licenses/ 下的 MIT 许可证全文和第三方声明复制到 dist/。

    KataGo 引擎和权重都是 MIT License，要求保留版权声明和许可证全文。
    玩家安装游戏后能在 dist/ 根目录和 KataGo 子目录看到 License.txt。
    """
    if not os.path.isdir(LICENSES_SRC):
        print(f"[!] 找不到合规材料源目录：{LICENSES_SRC}")
        print("    跳过许可证同步（建议补上以满足 MIT 许可要求）")
        return False

    copied = 0
    for root, dirs, files in os.walk(LICENSES_SRC):
        rel = os.path.relpath(root, LICENSES_SRC)
        out_dir = DIST_DIR if rel == "." else os.path.join(DIST_DIR, rel)
        os.makedirs(out_dir, exist_ok=True)
        for f in files:
            src_file = os.path.join(root, f)
            dst_file = os.path.join(out_dir, f)
            shutil.copy2(src_file, dst_file)
            copied += 1

    print(f"[1c] 合规材料已同步 {copied} 个文件")
    print(f"      THIRD-PARTY-NOTICES.txt -> {DIST_DIR}")
    print(f"      KataGo/License.txt      -> {DIST_DIR}/KataGo/")
    print(f"      KataGo/weights/License.txt -> {DIST_DIR}/KataGo/weights/")
    return True


# ---------------- 步骤 2：重建桌面快捷方式 ----------------
def get_pidl(path: str) -> bytes:
    pidl = shell32.ILCreateFromPathW(path)
    if not pidl:
        raise OSError(f"ILCreateFromPathW 失败: {path}")
    return ctypes.string_at(pidl, shell32.ILGetSize(pidl))


def build_lnk(target: str, work_dir: str, name: str, desc: str) -> bytes:
    idlist = get_pidl(target)

    # LinkTargetIDList: IDListSize(2) + IDList + 终止符(2)
    link_target_idlist = struct.pack("<H", len(idlist)) + idlist + b"\x00\x00"

    # LinkInfo
    local_base_path = target.encode("cp1252", errors="replace") + b"\x00"
    li_header_size = 0x1C
    li_body = struct.pack(
        "<IIIIIII",
        0, li_header_size, 0x01, 0, li_header_size, 0, 0,
    )
    li_body += local_base_path + b"\x00"
    li_body = struct.pack("<I", len(li_body)) + li_body[4:]

    # StringData（Unicode 模式）
    def ustr(s: str) -> bytes:
        a = s.encode("cp1252", errors="replace")
        u = s.encode("utf-16-le", errors="replace")
        return (struct.pack("<H", len(s) + 1) + u + b"\x00\x00" +
                struct.pack("<H", len(s) + 1) + a + b"\x00")

    name_block = ustr(name)
    rel_block = ustr(os.path.basename(target))
    work_block = ustr(work_dir)
    # 强制使用 installer/app.ico（避免 .NET apphost 无图标）
    icon_block = ustr(ICON_PATH if os.path.exists(ICON_PATH) else target)
    desc_block = ustr(desc)

    flags = (0x0001 | 0x0002 | 0x0004 | 0x0008 |
             0x0010 | 0x0040 | 0x0080)

    # Header（76 bytes；三个时间戳是 8 字节 FILETIME，写成 4 字节会导致快捷方式失效）
    clsid = bytes([0x01, 0x14, 0x02, 0x00, 0, 0, 0, 0,
                   0xC0, 0, 0, 0, 0, 0, 0, 0x46])
    header = struct.pack("<I", 0x4C) + clsid
    header += struct.pack("<I", flags)
    header += struct.pack("<I", 0x20)
    header += struct.pack("<QQQ", 0, 0, 0)
    header += struct.pack("<I", os.path.getsize(target))
    header += struct.pack("<I", 0)
    header += struct.pack("<I", 1)
    header += struct.pack("<H", 0)
    header += struct.pack("<H", 0)
    header += struct.pack("<II", 0, 0)
    assert len(header) == 0x4C, f"header 长度错误: {len(header)}"

    return (header + link_target_idlist + li_body +
            name_block + rel_block + work_block + icon_block + desc_block)


def create_shortcut():
    target = os.path.join(DIST_DIR, EXE_NAME)
    if not os.path.exists(target):
        print(f"[!] dist 里没有 {EXE_NAME}")
        return False

    desktop = os.path.join(os.environ["USERPROFILE"], "Desktop")
    lnk = os.path.join(desktop, LINK_NAME + ".lnk")

    # 清理旧版本名残留（彻底避免图标/名字双轨存在）
    for old_name in ("GoSmart · 人机围棋", "GoSmart - 人机围棋", "GoSmart",
                     "GoGame", "人机围棋"):
        old_lnk = os.path.join(desktop, old_name + ".lnk")
        if os.path.exists(old_lnk):
            try:
                os.remove(old_lnk)
                print(f"      已清理旧快捷方式: {old_name}.lnk")
            except OSError:
                pass

    data = build_lnk(target, DIST_DIR, LINK_NAME, DESCRIPTION)
    with open(lnk, "wb") as f:
        f.write(data)

    # 自检
    with open(lnk, "rb") as f:
        head = f.read(0x4C)
    hs = struct.unpack("<I", head[0:4])[0]
    fl = struct.unpack("<I", head[20:24])[0]
    ok = (hs == 0x4C) and bool(fl & 0x01)

    print(f"[2] 桌面快捷方式已重建")
    print(f"      {lnk}")
    print(f"      大小 {len(data)} bytes, HeaderSize=0x{hs:X}, LinkFlags=0x{fl:08X}")
    print(f"      图标: {ICON_PATH}")
    print(f"      自检: {'通过 [OK]' if ok else '失败 [FAIL]'}")
    return ok


def main():
    print("=" * 60)
    print("中国围棋 一键部署")
    print("=" * 60)
    if not sync_dist():
        sys.exit(1)
    print()
    sync_katago()
    print()
    sync_licenses()
    print()
    if not create_shortcut():
        sys.exit(1)
    print()
    print("完成。双击桌面「中国围棋」即可启动。")


if __name__ == "__main__":
    main()
