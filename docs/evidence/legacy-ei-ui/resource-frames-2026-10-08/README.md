# Legacy EI UI resource-frame evidence (2026-10-08)

These PNGs were selected from temporary decode outputs because the related UI audit refers to these source frames and the repository did not yet contain all of them. They are **resource-frame previews**, not gameplay screenshots and not proof of a control's runtime behavior.

| File | Source frame | WIL header size | Offset | Notes |
|---|---|---:|---:|---|
| [`Interface1c_F50.png`](Interface1c_F50.png) | `Interface1c.wil` F50 | 640×480 | (-24,-16) | Legacy character-selection background frame. |
| [`GameInter_F1000.png`](GameInter_F1000.png) | `GameInter.wil` F1000 | 512×512 | (+7,-44) | Store/window-family frame preview; exact business state should be established from the audit's code evidence, not appearance alone. |
| [`GameInter_F1002.png`](GameInter_F1002.png) | `GameInter.wil` F1002 | 1024×512 | (+7,-44) | Sibling frame retained for visual state comparison; semantics are not inferred here. |
| [`GameInter_F1003.png`](GameInter_F1003.png) | `GameInter.wil` F1003 | 512×512 | (+7,-44) | Sibling frame retained for visual state comparison; semantics are not inferred here. |

## Verification and exclusions

The temporary PNGs were decoded from the corresponding local WIL frames with the existing WIL reader. Their RGBA pixels, including alpha, matched a fresh independent decode of those exact frame indices. The existing `gameinter-frame-1001-wil-2026-09-24.png` was checked against the temporary F1001 preview and is pixel-identical in its visible/alpha content, so it was not duplicated here.

The original WIL/WIX libraries remain external runtime assets and were not copied. These extracted frames support visual review only; frame appearance does not establish hitboxes, state transitions, or interactive semantics. See [`LEGACY_EI_UI_AUDIT_2026-09-23.md`](../../../LEGACY_EI_UI_AUDIT_2026-09-23.md) for the broader audit and its evidence limits.