#!/usr/bin/env python3
"""verify_zl_repair.py — 独立校验 fix_zl_overlay_holes.py 产出的 ZL2 修复件。

校验项（对 original 与 repaired 逐帧比对）：
  1. 容器结构：索引条目数量 / 顺序 / Id / Type / Compression / Codec 完全一致；
     每个 payload 都能按各自压缩方式解压，长度 == UncompressedSize。
  2. metadata：帧数、Position/Width/Height/Offset/Overlay 尺寸/Shadow 尺寸一致；
     只有被修复帧的 ImageCodec / StoredImageDataSize 允许变化（BC7 -> PNG）。
  3. 像素：逐帧解码 Image 与 Overlay 层。
     - 未修复帧：两版必须逐字节相同。
     - 修复帧：Overlay 层必须相同；Image 层只允许「原 alpha=0 且 Overlay alpha>0」
       的像素发生变化，其余像素逐字节相同；修复后这些像素 alpha 必须为 255。
     - 修复后不得再存在「Image 透明但 Overlay 不透明」的像素。
用法： verify_zl_repair.py <original.Zl> <repaired.Zl>
"""
from __future__ import annotations

import struct
import sys
import zlib

import numpy as np

sys.path.insert(0, str(__import__("pathlib").Path(__file__).resolve().parent))
from fix_zl_overlay_holes import (CODEC_BC7, CODEC_PNG, Zl2, holes_of)  # noqa: E402


def main(orig_path: str, new_path: str) -> int:
    a, b = Zl2(orig_path), Zl2(new_path)
    errors: list[str] = []

    if (a.meta_count, a.image_count, a.atlas_count) != (b.meta_count, b.image_count, b.atlas_count):
        errors.append("metadata header count/atlas 不一致")
    if a.entry_order != b.entry_order:
        errors.append("索引条目顺序/Id 不一致")
    if len(a.frames) != len(b.frames):
        errors.append("帧数不一致")

    repaired, unchanged = 0, 0
    for i, fa in sorted(a.frames.items()):
        fb = b.frames.get(i)
        if fb is None:
            errors.append(f"frame {i} 在修复件中缺失")
            continue
        for field in ("pos", "w", "h", "ox", "oy", "ovl_size", "shadow_size", "ovl_codec"):
            va, vb = getattr(fa, field), getattr(fb, field)
            if va != vb:
                errors.append(f"frame {i} {field}: {va} -> {vb}")
        changed = (fa.img_codec != fb.img_codec) or (fa.img_size != fb.img_size)
        if not changed:
            unchanged += 1
            if (fa.img_codec, fa.img_size, fa.bc7_size, fa.fb_size) != \
               (fb.img_codec, fb.img_size, fb.bc7_size, fb.fb_size):
                errors.append(f"frame {i} 未修复却改了 Image 载荷元数据")
        else:
            repaired += 1
            if fa.img_codec != CODEC_BC7 or fb.img_codec != CODEC_PNG:
                errors.append(f"frame {i} codec 变化异常 {fa.img_codec} -> {fb.img_codec}")
            if fb.bc7_size != 0 or fb.fb_size != 0:
                errors.append(f"frame {i} 修复后仍有 Bc7/Fallback 段")
        if fa.pos < 0 or fa.pos not in a.entries:
            continue

        raw_a, raw_b = a.payload(fa.pos), b.payload(fb.pos)
        if a.entries[fa.pos]["usize"] != len(raw_a) or b.entries[fb.pos]["usize"] != len(raw_b):
            errors.append(f"frame {i} UncompressedSize 与解压长度不符")

        img_a = a.layer(fa, raw_a, "image")
        img_b = b.layer(fb, raw_b, "image")
        ovl_a = a.layer(fa, raw_a, "overlay")
        ovl_b = b.layer(fb, raw_b, "overlay")
        for name, x, y in (("image", img_a, img_b), ("overlay", ovl_a, ovl_b)):
            if (x is None) != (y is None):
                errors.append(f"frame {i} {name} 层存在性变化")
        if img_a is None:
            continue
        if ovl_a is not None and not np.array_equal(ovl_a, ovl_b):
            errors.append(f"frame {i} overlay 层像素被改动")

        diff = img_a != img_b
        if not changed:
            if diff.any():
                errors.append(f"frame {i} 未修复却改了 image 像素 ({int(diff.any(axis=2).sum())} px)")
            continue
        hole = holes_of(img_a, ovl_a)
        mask = diff.any(axis=2)
        allowed = hole if hole is not None else np.zeros_like(mask)
        if (mask & ~allowed).any():
            errors.append(f"frame {i} 修复改到了非空洞像素 ({int((mask & ~allowed).sum())} px)")
        if hole is not None and (img_b[hole][:, 3] != 255).any():
            errors.append(f"frame {i} 修复后空洞像素 alpha 非 255")
        if holes_of(img_b, ovl_b) is not None and holes_of(img_b, ovl_b).any():
            errors.append(f"frame {i} 修复后仍存在被 overlay 覆盖的空洞")

    print(f"original={orig_path} repaired={new_path}")
    print(f"frames: repaired={repaired} unchanged={unchanged} entries={len(a.entry_order)}")
    if errors:
        print(f"FAIL: {len(errors)} 项")
        for e in errors[:40]:
            print("  -", e)
        return 1
    print("PASS: 结构/元数据/像素全部符合修复契约")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
