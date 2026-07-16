# PasteClip for Windows — P0 / P1 Feature Roadmap

**Status:** Draft v0.1
**Date:** 2026-07-15
**Companion docs:** [PDD](01-PDD.md), [HLD](02-HLD.md), [Impl Plan](03-impl-plan.md)

> **Terminology:** here **P0 / P1** are *release/feature tiers* (product priority), not the
> engineering phase numbers used in the [Implementation Plan](03-impl-plan.md) (which go P0–P8).
> To map: this roadmap's **P0** ≈ impl phases P0–P2 (+ a slice of P1); this roadmap's
> **P1** ≈ impl phases P3–P8.

---

## Guiding cut
**P0 = the smallest thing that is actually a useful clipboard manager and ships in one
iteration.** If a feature isn't required for "copy stuff → hotkey → find it → paste it,"
it's P1. Privacy identity is preserved by keeping local encrypted storage in P0, but every
*management/configuration* surface is deferred.

---

## P0 — Core MVP (single iteration)

**Theme: the capture → recall → paste loop, done natively and privately.**

| # | Feature | Why it's core |
|---|---|---|
| C1 | **Text clipboard capture** (event-driven, `WM_CLIPBOARDUPDATE`) | Without capture there's no product |
| C2 | **Local encrypted storage** (SQLite + DPAPI-wrapped key) | Privacy-first is the identity; can't ship plaintext history |
| C3 | **Recent history list** (reverse-chronological) | The one collection you can't live without |
| C4 | **Global hotkey → recall overlay** (default `Win+Alt+V`) | The primary way users reach the app |
| C5 | **Live search** over history (substring) | History is useless if you can't find a clip |
| C6 | **Paste-back** (set clipboard + SendInput into prior app) | Closes the loop; the payoff action |
| C7 | **Keyboard navigation** in overlay (↑/↓/Enter/Esc) | Keyboard-first is a core promise |
| C8 | **Consecutive dedupe + size/count caps** | Keeps history sane and store bounded |
| C9 | **Tray icon** (open, quit) | Basic lifecycle for an always-on utility |
| C10 | **Manual delete of a clip** + **clear all** | Minimum privacy hygiene without a full settings UI |

**Explicitly NOT in P0:** Pinned, Templates, exclusions, redaction rules, retention timer,
settings UI, hotkey rebinding, export/import, autostart, theming, images/files.

**P0 hardcoded defaults (no settings UI yet):**
- Hotkey fixed at `Win+Alt+V` (rebinding is P1).
- Retention: none in P0 — rely on the count cap (e.g. keep last N); the *timed* 7-day
  purge is P1. (Acceptable short-term because "clear all" exists.)
- Text-only (`CF_UNICODETEXT`).
- Follows system light/dark; no theme picker.

**P0 exit / demo:** Copy text in any app → press `Win+Alt+V` → type to filter → Enter →
it pastes into the app you were in. History is encrypted at rest. Nothing touches the network.

---

## P1 — Complete the v1 vision

**Theme: organization, privacy controls, and configurability.**

| Group | Features |
|---|---|
| **Collections** | **Pinned** (promote clips, exempt from purge); **Templates** (named reusable snippets, never auto-captured); collection filters in overlay + main window |
| **Privacy controls** | **Pause/resume capture**; **per-app exclusion list**; **redaction rules** (skip/mask card- and token-like patterns, defaults on); **timed retention** (default 7-day purge, honoring pin/template exemption); **secure "delete all now"** + VACUUM; Privacy info screen |
| **Configurability** | **Settings UI**; **hotkey rebinding** + conflict handling; **launch-at-startup**; **theme** (light/dark/high-contrast); adjustable caps & retention window |
| **Portability** | **Encrypted JSON export/import** (passphrase; merge vs. replace) |
| **UX polish** | Main history browser window; source-app + timestamp display; "paste as plain text" modifier; fuzzy search; near-caret overlay placement with cursor fallback |
| **Robustness** | Clipboard-lock retry/backoff; DB corruption detection + restore-from-export; no-network CI gate; accessibility pass; perf tuning to NFR targets |

---

## Deferred beyond P1 (v2+, unchanged)
- Image & file clips (`CF_DIB`, `CF_HDROP`).
- Template variables / snippet expansion.
- Clip transforms (uppercase, trim, JSON pretty-print).
- Multi-hotkey profiles, richer previews.

---

## Sequencing rationale
1. **P0 is vertical, not horizontal** — it walks the full stack (interop → storage →
   UI → paste) for *one* narrow flow, proving the risky Win32 pieces early rather than
   building breadth on unproven interop.
2. **Privacy floor stays in P0** (encryption + clear-all) so we never ship a version that
   contradicts the product's identity, even before the full privacy *control panel* exists.
3. **Everything configurable is P1** — a settings UI is a multiplier of work; hardcoding
   sane defaults lets P0 land in one iteration.
4. **P1 groups are independently shippable** — Collections, Privacy controls, Configurability,
   and Portability can each land as their own increment after P0.

## Open decisions that affect the cut
- **Timed retention in P0 or P1?** Recommended P1 (count cap suffices short-term). If a
  reviewer considers timed purge part of the privacy floor, promote just the purge job
  (not its settings UI) into P0.
- **Encryption approach** (SQLCipher vs. field-level AES) must be settled in P0 since it's
  in the P0 storage feature (see impl-plan decision **D1**).
