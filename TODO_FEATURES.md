# Feature Implementation Plan

## Phase 1: Quick Wins (High Impact, Low Effort)
- [x] Enable Native AOT (test thoroughly) - **DONE: 72.3 MB, tests pass, app runs**
- [x] Animated emote support (GIF/WebP in WebView2) - **DONE: BTTV/FFZ/7TV animated handling**
- [x] Chat log export (JSONL/CSV) - **DONE: ChatLogExporter service**
- [x] ETW/EventSource logging - **DONE: MultiChatEventSource with connection, message, emote, perf events**

## Phase 2: Core Features (Medium Impact)
- [ ] Profile system (multiple streamer configs)
- [ ] Message filtering/moderation (regex allow/block, user ignore)
- [ ] Custom CSS/themes (user-editable CSS injection)
- [ ] OBS Browser Source export (standalone HTML+JS)
- [ ] Auto-hide on inactivity (fade overlay)

## Phase 3: Quality of Life
- [ ] Runtime source toggle (enable/disable without restart)
- [ ] TTS integration (Windows SpeechSynthesis or Piper)
- [ ] Analytics overlay (msg/min, unique chatters, top emotes)
- [ ] Discord webhook relay
- [ ] Global hotkeys (RegisterHotKey)

## Phase 4: Architecture Hardening
- [ ] Plugin interface (formalize IChatSource as NuGet)
- [ ] Cross-platform message deduplication
- [ ] Health-based adaptive polling for all HTTP sources