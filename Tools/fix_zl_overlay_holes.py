#!/usr/bin/env python3
"""fix_zl_overlay_holes.py — 修复 ZL2 图库中「Image 层丢失 overlay 色」的帧。

背景（详见 docs/ITEM_ICON_ALPHA_DEFECT_2026-10-05.md）：
Data/*.Zl 是由 WIL 转出的 BC7 重编码。对带「overlay colour」(WIL RLE opcode
0xC2) 的帧，转换器把这些像素从 Image 层删掉了（Image alpha=0），只保留在
Overlay 层里。原版客户端画物品图标只用 Image 层（Client/Controls/DXItemCell.cs
-> ImageType.Image），于是这些像素整块变透明 —— 实测 Inventory.Zl / Equip.Zl
各 85 帧，透明像素占比 25% -> 47%，肉眼就是「衣服手臂/侧摆被截掉 + 残留虚线」。

本工具不依赖 WIL 源：同一帧的 Overlay 层就是那批像素的（略暗的）副本。
Image ∪ Overlay 即完整原画（离线对照 WIL 源验证：形状 100% 一致，
颜色略暗 ≤16%）。修复 = 把 Overlay 的不透明像素补进 Image 层，Image 层改用
PNG（无损）载荷，容器其余部分（Overlay 载荷 / metadata / 帧序）原样保留。

用法：
  fix_zl_overlay_holes.py scan      <lib.Zl>
  fix_zl_overlay_holes.py fix       <lib.Zl> <out.Zl> [--dry-run]
  fix_zl_overlay_holes.py roundtrip <lib.Zl> <out.Zl>   # 只重排容器，不改像素

纪律：先写 <out.Zl>，再用 zlsdk/GodotClient 独立读回逐帧比对，通过后才替换。
"""
from __future__ import annotations

import argparse
import io
import struct
import sys
import zlib
from pathlib import Path

import numpy as np
import texture2ddecoder
from PIL import Image

CODEC_DXT1, CODEC_DXT5, CODEC_BGRA32, CODEC_BC7, CODEC_PNG = 0, 1, 2, 3, 4
COMP_NONE, COMP_DEFLATE_FAST, COMP_DEFLATE_BEST = 0, 1, 2


def raw_deflate(raw: bytes) -> bytes:
    co = zlib.compressobj(6, zlib.DEFLATED, -15)
    return co.compress(raw) + co.flush()


class ZlFrame:
    __slots__ = ("index", "pos", "w", "h", "ox", "oy", "meta_offset",
                 "img_size", "bc7_size", "fb_size", "ovl_size",
                 "shadow_size", "img_codec", "ovl_codec")


class Zl2:
    """Minimal ZL2 container reader/writer (mirrors GodotClient ZlReader)."""

    def __init__(self, path: str):
        self.path = path
        self.data = bytearray(Path(path).read_bytes())
        d = self.data
        if bytes(d[0:3]) != b"ZL2":
            raise ValueError(f"{path} 不是 ZL2 容器（legacy 格式不支持本修复）")
        (self.version, self.image_count, self.atlas_count) = struct.unpack_from("<iii", d, 3)
        self.default_compression = d[15]
        self.flags = d[16]
        (self.meta_off, self.meta_size) = struct.unpack_from("<qi", d, 19)
        (self.index_off, self.index_size) = struct.unpack_from("<qi", d, 31)
        self.meta = bytearray(d[self.meta_off:self.meta_off + self.meta_size])
        self._read_meta()
        self._read_index()
        self.by_pos = {f.pos: f for f in self.frames.values() if f.pos >= 0}

    # -- metadata ---------------------------------------------------------
    def _read_meta(self):
        m = self.meta
        (self.meta_ver, self.meta_count, self.meta_agic, self.meta_aps) = struct.unpack_from("<iiii", m, 0)
        self.frames: dict[int, ZlFrame] = {}
        p = 16
        for i in range(self.meta_count):
            pres = m[p]
            p += 1
            if not pres:
                continue
            f = ZlFrame()
            f.index = i
            f.meta_offset = p - 1
            (f.pos, f.w, f.h, f.ox, f.oy) = struct.unpack_from("<ihhhh", m, p)
            p += 12
            p += 1 + 8                      # ShadowType + shadow w/h/ox/oy
            p += 4                          # overlay w/h
            p += 4 + 8 + 8                  # atlas page, src rect, vis rect
            f.img_codec, f.ovl_codec = m[p], m[p + 2]
            p += 3 + 3                      # codecs + runtime prefs
            (f.img_size, f.bc7_size, f.fb_size,
             f.shadow_size, _sb, _sf,
             f.ovl_size, _ob, _of) = struct.unpack_from("<9i", m, p)
            p += 36
            self.frames[i] = f
        assert p == len(m), (p, len(m))

    def set_image_payload_meta(self, f: ZlFrame, codec: int, stored: int):
        """只改 Image 层 codec/size；Overlay/Shadow 与 runtime preference 原样保留
        （RenderingCore 对非 Png codec 不看 preference；Png 落到 Bgra32）。"""
        m = self.meta
        p = f.meta_offset + 1 + 12 + 1 + 8 + 4 + 4 + 8 + 8
        assert m[p] == f.img_codec and m[p + 2] == f.ovl_codec, (m[p], f.img_codec)
        m[p] = codec
        p += 3 + 3
        struct.pack_into("<3i", m, p, stored, 0, 0)
        f.img_codec, f.img_size, f.bc7_size, f.fb_size = codec, stored, 0, 0

    # -- index ------------------------------------------------------------
    def _read_index(self):
        d = self.data
        n = struct.unpack_from("<i", d, self.index_off)[0]
        p = self.index_off + 4
        self.entries: dict[int, dict] = {}
        self.entry_order: list[int] = []
        for _ in range(n):
            t = d[p]
            eid, usize, csize, off = struct.unpack_from("<iiiq", d, p + 1)
            comp, codec = d[p + 21], d[p + 22]
            self.entries[eid] = dict(type=t, usize=usize, csize=csize, off=off,
                                     comp=comp, codec=codec)
            self.entry_order.append(eid)
            p += 23

    def payload(self, eid: int) -> bytes:
        e = self.entries[eid]
        raw = bytes(self.data[e["off"]:e["off"] + e["csize"]])
        return zlib.decompress(raw, -15) if e["comp"] else raw

    # -- layers -----------------------------------------------------------
    @staticmethod
    def decode_bc7(seg: bytes, w: int, h: int):
        px = texture2ddecoder.decode_bc7(seg, w, h)
        return np.frombuffer(px, dtype=np.uint8).reshape(h, w, 4)[:, :, [2, 1, 0, 3]]

    def layer(self, f: ZlFrame, raw: bytes, which: str):
        """解码 Image / Overlay 层 -> RGBA ndarray（支持 BC7 与 PNG 载荷）。"""
        if which == "image":
            start, size, codec, w, h = 0, f.img_size, f.img_codec, f.w, f.h
        else:
            start = f.img_size + f.bc7_size + f.fb_size + f.shadow_size
            size, codec, w, h = f.ovl_size, f.ovl_codec, f.w, f.h
        if size <= 0 or w <= 0 or h <= 0 or start + size > len(raw):
            return None
        seg = raw[start:start + size]
        if codec == CODEC_PNG:
            img = Image.open(io.BytesIO(seg)).convert("RGBA")
            return np.asarray(img, dtype=np.uint8) if img.size == (w, h) else None
        if codec == CODEC_BC7:
            return self.decode_bc7(seg, w, h)
        return None


def png_bytes(rgba: np.ndarray) -> bytes:
    buf = io.BytesIO()
    Image.frombytes("RGBA", (rgba.shape[1], rgba.shape[0]), rgba.astype(np.uint8).tobytes()) \
        .save(buf, format="PNG", optimize=False, compress_level=9)
    return buf.getvalue()


def holes_of(img, ovl):
    if img is None or ovl is None or img.shape != ovl.shape:
        return None
    return (img[..., 3] == 0) & (ovl[..., 3] > 0)


def repair_frame(zl: Zl2, f: ZlFrame):
    """返回 (新的 Image 层 PNG 字节, 修补像素数)；无需修补返回 (None, 0)。"""
    if f.img_codec != CODEC_BC7 or f.ovl_size <= 0:
        return None, 0
    raw = zl.payload(f.pos)
    img = zl.layer(f, raw, "image")
    ovl = zl.layer(f, raw, "overlay")
    hole = holes_of(img, ovl)
    if hole is None or not hole.any():
        return None, 0
    out = img.copy()
    out[hole, 0:3] = ovl[hole][:, 0:3]
    out[hole, 3] = 255
    return png_bytes(out), int(hole.sum())


def rebuild(zl: Zl2, out_path: str, replacements: dict[int, bytes], dry_run: bool):
    """按原容器顺序重排 payload；只替换 replacements{entry_id: 新 image payload}。"""
    header = bytes(zl.data[:zl.meta_off])
    meta = zl.meta
    data = bytearray()
    new_entries = []
    for eid in zl.entry_order:
        e = zl.entries[eid]
        raw = zl.payload(eid)
        if eid in replacements:
            png = replacements[eid]
            f = zl.by_pos[eid]
            old_img_len = f.img_size + f.bc7_size + f.fb_size
            raw = png + raw[old_img_len:]
            zl.set_image_payload_meta(f, CODEC_PNG, len(png))
        payload = raw if e["comp"] == COMP_NONE else raw_deflate(raw)
        new_entries.append(dict(eid=eid, type=e["type"], comp=e["comp"], codec=e["codec"],
                                off=len(data), csize=len(payload), usize=len(raw)))
        data += payload

    if dry_run:
        return len(data), new_entries

    data_off = zl.meta_off + len(meta)
    index = bytearray(struct.pack("<i", len(new_entries)))
    for e in new_entries:
        index += struct.pack("<Bi", e["type"], e["eid"])
        index += struct.pack("<iiqBB", e["usize"], e["csize"], e["off"] + data_off,
                             e["comp"], e["codec"])
    index_off = data_off + len(data)

    blob = bytearray(header)   # 43 bytes；meta_off / meta_size 保持不变
    blob += meta
    blob += data
    blob += index
    struct.pack_into("<qi", blob, 31, index_off, len(index))
    Path(out_path).write_bytes(bytes(blob))
    return len(data), new_entries


def affected_frames(zl: Zl2):
    for i, f in sorted(zl.frames.items()):
        if f.ovl_size <= 0 or f.pos < 0 or f.pos not in zl.entries:
            continue
        if f.img_codec != CODEC_BC7:
            continue
        raw = zl.payload(f.pos)
        hole = holes_of(zl.layer(f, raw, "image"), zl.layer(f, raw, "overlay"))
        if hole is not None and hole.any():
            yield i, f, int(hole.sum())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["scan", "fix", "roundtrip"])
    ap.add_argument("lib")
    ap.add_argument("out", nargs="?")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    zl = Zl2(a.lib)
    if a.mode == "scan":
        n = 0
        for i, f, px in affected_frames(zl):
            print(f"  frame {i}: {px} px  {f.w}x{f.h}  ovl={f.ovl_size}B")
            n += 1
        print(f"{a.lib}: {n} frames with overlay-covered holes (entries={len(zl.entries)})")
        return 0

    replacements = {}
    total_px = 0
    if a.mode == "fix":
        for i, f, px in affected_frames(zl):
            png, n = repair_frame(zl, f)
            if png is None:
                continue
            if f.pos in replacements:
                raise SystemExit(f"duplicate entry id {f.pos}")
            replacements[f.pos] = png
            total_px += n
            print(f"  frame {i}: +{n} px -> PNG {len(png)}B (BC7 was {f.img_size}B)")
    print(f"{a.lib}: {a.mode} frames={len(replacements)} pixels={total_px}")
    size, ents = rebuild(zl, a.out, replacements, a.dry_run)
    print(f"{a.mode}: out={a.out} data={size}B entries={len(ents)} dry_run={a.dry_run}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
