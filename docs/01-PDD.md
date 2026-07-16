# PasteClip for Windows — Product Definition Document (PDD)

**Status:** Draft v0.1
**Date:** 2026-07-15
**Product codename:** *Past* (working title)

---

## 1. Summary

A privacy-first, local-only clipboard manager for Windows, inspired by the macOS
app **PasteClip**. It captures clipboard history on-device, lets the user recall
past clips instantly via a global hotkey and search, and organizes clips into
**Recent / Pinned / Templates**. **Free software** — no account, no cloud, no
telemetry, no pricing, no monetization. Portability is handled solely through
encrypted JSON export/import.

This is a *re-implementation*, not a port: the macOS APIs (NSPasteboard, iCloud,
AppKit) are replaced with their Windows equivalents (Win32 clipboard, WinUI 3,
DPAPI, local SQLite). Feature intent is preserved; implementation is native.

## 2. Goals & Non-Goals

### Goals
- G1. Reliably capture text (and later, images/files) copied to the Windows clipboard.
- G2. Instant recall: global hotkey → search → paste in under 2 seconds, keyboard-only.
- G3. Organize clips into Recent, Pinned, and Templates.
- G4. Strong privacy defaults: local storage, capture pausing, per-app exclusion,
  sensitive-pattern redaction, bounded retention.
- G5. Lightweight always-on utility: low idle RAM/CPU, starts with Windows, lives in tray.
- G6. Portability via JSON export/import (encrypted).

### Non-Goals (explicitly out of scope)
- N1. **Cloud sync of any kind, ever.** No iCloud analogue, no OneDrive, no account. (Per product decision.)
- N2. Cross-device / mobile companion apps.
- N3. Team/shared clipboards or collaboration.
- N4. Telemetry, analytics, or crash reporting that leaves the device.
- N5. **Any platform other than Windows.** No macOS, Linux, web, or mobile builds,
  and no cross-platform/hybrid framework abstraction to enable them later. The app
  is built directly against Windows-native APIs; portability across OSes is a
  non-requirement, not a deferred feature.
- N6. **Pricing, monetization, in-app purchases, licensing servers, or any remote
  service.** This is free software with no revenue mechanics.

## 3. Target Users & Personas

- **Keyboard-first power user / developer** — copies snippets, commands, tokens all
  day; wants sub-second recall without touching the mouse; cares about not leaking
  secrets into a persistent store.
- **Privacy-conscious professional** — lawyer, healthcare, finance; needs the tool
  to *not* retain sensitive data and to exclude specific apps (password managers).
- **Writer / knowledge worker** — reuses boilerplate (Templates), manages research
  snippets (Pinned), wants quick visual history.

## 4. Reference App Feature Map (PasteClip → Windows)

| PasteClip (macOS) | Windows equivalent behavior | v1? |
|---|---|---|
| Recent / Pinned / Templates | Same three collections | ✅ |
| Global shortcut ⌘⇧V | Global hotkey (default **Win+Alt+V**, remappable*) | ✅ |
| Instant search & filters | Live substring/fuzzy search + type filters | ✅ |
| Local, on-device storage | Local SQLite in `%LOCALAPPDATA%` | ✅ |
| Pause capture | Toggle capture on/off | ✅ |
| Exclude apps | Per-process exclusion list | ✅ |
| Auto-redact sensitive patterns | Regex/pattern rules to skip or mask | ✅ |
| 1-week default retention | Configurable retention (default 7 days) | ✅ |
| JSON export/import backup | Encrypted JSON export/import | ✅ |
| iCloud sync | **Dropped** (non-goal N1) | ❌ |
| $4.99 one-time | **Dropped** — free software, no pricing (non-goal N6) | ❌ |

\* Note: `Win+V` is reserved by the Windows built-in Clipboard History, and `Win+Shift+V`
was found to be **already taken** on real hardware (RegisterHotKey → 1409). We therefore
try an ordered candidate list and use the first that registers; `Win+Alt+V` is first.
`Ctrl+Shift+V` is deliberately excluded from the defaults: it registers fine globally but
would hijack "paste as plain text" inside every other app. See HLD for the strategy.

## 5. Functional Requirements

### 5.1 Capture
- FR1. Monitor the system clipboard and record new clips (text in v1; images/files v2).
- FR2. De-duplicate consecutive identical clips; move an existing identical clip to top.
- FR3. Record metadata: timestamp, source app (process name/path), content type, size, hash.
- FR4. Respect capture state: when paused, capture nothing.
- FR5. Respect exclusion list: never capture while the foreground app is excluded.
- FR6. Apply redaction rules before persisting (skip entirely or store masked).

### 5.2 Organize
- FR7. Recent: reverse-chronological capture list, subject to retention.
- FR8. Pinned: user-promoted clips, exempt from retention purge.
- FR9. Templates: user-authored reusable snippets (name + body), never auto-captured.

### 5.3 Recall & Paste
- FR10. Global hotkey opens a quick-recall overlay near the caret/cursor.
- FR11. Live search across all collections; keyboard navigation (↑/↓, Enter to paste).
- FR12. "Paste" restores the clip to the clipboard and sends paste to the prior foreground app.
- FR13. Optional "paste as plain text" modifier.
- FR14. Filters: by type, by collection, by source app.

### 5.4 Manage
- FR15. Pin/unpin, delete, edit (for templates), copy-to-clipboard without paste.
- FR16. Bulk delete / clear history / "delete all now" (privacy panic action).
- FR17. Settings: hotkey, retention window, exclusions, redaction rules, launch-at-startup,
  theme, max item size, max item count.

### 5.5 Portability
- FR18. Export encrypted JSON (user passphrase). Import merges or replaces.

## 6. Non-Functional Requirements
- NFR1. **Privacy:** all data on-device; DB encrypted at rest (DPAPI-protected key). No network calls at all in v1 (verifiable — app makes zero outbound connections).
- NFR2. **Performance:** idle RAM < 80 MB; overlay open < 150 ms; search over 10k clips < 50 ms.
- NFR3. **Reliability:** capture must not lose clips under rapid successive copies; survive clipboard-owner crashes.
- NFR4. **Footprint:** installer < 60 MB; starts within 1s of login without blocking shell.
- NFR5. **Security:** never log clip contents; secrets redaction is best-effort but on by default for common patterns.
- NFR6. **Accessibility:** full keyboard operability; high-contrast/theme support; screen-reader labels on overlay.
- NFR7. **Compatibility:** Windows 10 21H2+ and Windows 11.

## 7. Privacy & Security Model (product-level)
- Local-only by construction; the app ships with **no networking code paths** in v1.
- Data at rest: SQLite DB + a key protected via Windows DPAPI (per-user).
- Redaction defaults: patterns for credit-card-like numbers, API-key-like tokens,
  and entries originating from known password managers (excluded by default).
- Retention: default 7-day purge for Recent; Pinned/Templates never auto-purged.
- Panic controls: pause capture, exclude foreground app quickly, "delete all now".
- Transparency: an in-app "Privacy" screen states exactly what is stored and where.

## 8. Success Metrics (local, non-telemetry proxies)
Because we ship no telemetry, success is validated via beta feedback & manual dogfooding, not phone-home metrics:
- Time-to-paste (measured in usability sessions) median < 2s.
- Zero data-loss reports under stress test (rapid copy loop).
- No captured secrets in DB during redaction test suite.
- Idle resource targets met (NFR2/NFR4) on reference hardware.

## 9. Open Questions
- OQ1. Distribution: MSIX + winget vs. plain signed installer. (Microsoft Store's sandbox limits global hotkey/clipboard/paste-back; free distribution doesn't require it — see HLD packaging decision.)
- OQ2. Default hotkey final choice given `Win+V` conflict.
- OQ3. Should v1 capture images, or is text-only acceptable for first release? (Recommended: text-only v1.)
- OQ4. Open-source the code (license choice) or ship free-but-closed binaries. (No revenue implications either way; pure distribution/community choice.)

## 10. Release Phasing (product view)
- **v1 (MVP):** text capture, 3 collections, search, hotkey overlay, privacy controls, retention, encrypted export/import. Local-only.
- **v2:** image & file clips, richer template variables, better fuzzy search, rich previews.
- **v3:** power features — snippet expansion, quick actions/transforms on clips (uppercase, trim, JSON pretty-print), multi-hotkey profiles.

---

### Sources
- [PasteClip — Privacy-First Mac Clipboard Manager](https://pasteclip.app/)
- [PasteClip (open-source Paste alternative) — GitHub](https://github.com/minsang-alt/PasteClip)
- [Paste — Clipboard Manager for Mac](https://pasteapp.io/)
