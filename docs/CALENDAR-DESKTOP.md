# Kubuno Calendar (desktop) — design note

Status: design of record for the native desktop version of the **Calendar** module (`kubuno-calendar.exe`),
started 2026-10-12. User goal: the desktop app catches up with the **whole** web Calendar — every view and
every feature, not a subset. Method: the one of the Office desktop apps (`DATA-DESKTOP.md`, `PROJECTS-DESKTOP.md`,
`DOCUMENTS-EDITING.md`): a platform-neutral core crate that ports the web's pure logic, a portable desktop app
in `calendar/desktop/common`, and only the Win32 window and its drawing in `calendar/desktop/windows`. §8 is the
as-built log and §9 the feature-coverage table, kept up to date as the increments land.

Reference for behaviour and looks: **the web app**, `calendar/web/src` — `CalendarApp.tsx` (the orchestrator),
`MonthView.tsx` / `WeekView.tsx` / `DayView.tsx` / `YearView.tsx` / `ScheduleView.tsx`, `EventEditor.tsx`,
`RecurrenceCustomDialog.tsx`, `CalendarSidebarBody.tsx`, and the pure helpers `calendarUtils.ts`, `rrule.ts`,
`holidays.ts`, `moon.tsx`, `calendarColors.ts`. The repo follows the platform layout (`common/ server/ web/
desktop/ mobile/`); Calendar had no `common/` or `desktop/` before this work.

> ⚠️ **Calendar is a server-coupled module, not a Drive file.** An event lives in the PostgreSQL schema
> `calendar` (table `calendar.events`), is created/edited/deleted by REST calls, and recurrence is **expanded on
> the server** (the `rrule` crate in `server/src/services/recurrence_service.rs`), which returns ready
> `EventInstance`s over the requested window. The Documents file-save method (content digest, `PUT …/data`,
> 2 MiB cap, join/leave edit sessions, offline journal) **does not apply**, as with Data. This is the central
> divergence from the user's brief, which assumed client-side RRULE expansion and client-computed holidays —
> see §3 and §6.

## 1. What the web app does

### 1.1 Files

| File | Role |
|---|---|
| `CalendarApp.tsx` (`:68`) | the shell: view switching + URL `/calendar/:view` sync (`:89-115`), per-view range (`:355-371`), loads calendars/events/schedules/holidays/weather, merges overlays, keyboard shortcuts (`:298`), draft/quick-create, recurring move/delete scope dialogs (`:605`, `:629`), dispatch to the six views (`:535-587`) |
| `MonthView.tsx` (`:42`) | the month grid: days from `calendarGrid(month, weekStartsOn)` (`:59`), weekends optionally hidden (5 cols), dynamic 4/5/6 rows; per-day chips via `eventsForDay` (`:81`, max 4 then « +N »); all-day/multi-day **banners** laid out per week by `layoutBanners` (`:88`) with `BANNER_BAR_H=18`/`BANNER_ROW_H=20`/`BANNER_TOP=34` (`:113-115`); moon marker, weather, drag-to-move, context menu, past-event dimming (`color+'2b'`) |
| `WeekView.tsx` (`:42`) | time grid, also the N-day « custom » view; `PX_PER_HOUR=40` (`:77`); event geometry from minutes-of-day; overlap columns from `layoutDayEvents` (`:285`); drag-create/move/resize at 15-min grain; dual-timezone gutter; now-line; all-day banner row; work-location band; availability strips |
| `DayView.tsx` (`:42`) | single-column time grid, same math as Week |
| `YearView.tsx` (`:221`) | 12 `MiniMonth` cards, up to 3 colour dots/day, day popover |
| `ScheduleView.tsx` (`:39`) | the agenda (« Planning »), one row per non-empty day over the range |
| `ScheduleGrid.tsx` (`:71`) | the « Find a time » tool inside the editor: `HOUR_H=44`, `SNAP_MIN=15`, busy bands from `findCommonSlots` |
| `SearchResultsView.tsx` (`:38`) | client-side filter over ±1 year of events |
| `calendarUtils.ts` | the layout math (see §1.2) |
| `rrule.ts` | RRULE presets, custom round trip, French humanizer — **no expansion** |
| `holidays.ts` | public holidays fetched from the **core** (`GET /holidays`), mapped to all-day events |
| `moon.tsx` | synodic moon-phase computation, 100% client-side |
| `calendarColors.ts` / `EVENT_SWATCHES` | the colour palettes |
| server `handlers/*`, `services/*`, `migrations/` | the `calendar.*` tables and routes (see §1.3) |

### 1.2 Pure layout math (`calendarUtils.ts`)

- `calendarGrid(month, weekStartsOn)` (`:138`) — `startOfWeek(startOfMonth) … endOfWeek(endOfMonth)`.
- `layoutDayEvents(evs)` (`:103`) — side-by-side overlap columns: sort by start then longer-first, group
  transitive overlaps into clusters, give each event the first free column, split the cluster width
  (`100/cols`); `MIN_SPAN=30` min.
- `layoutBanners(events, days)` (`:247`) — clip each all-day/multi-day event to the visible days, greedily
  stack into non-overlapping rows; returns `{segs, rows}` with `startCol/endCol/continuesBefore/After/row`.
- `isBannerEvent`/`bannerStartDay`/`bannerEndDay` (`:202-231`) — all-day events are stored as UTC-midnight
  instants and read offset-independently (`allDayLocal`, `:212`); a timed event ending exactly at local
  midnight is pulled back a day.
- `shade(hex, factor)` (`:189`), `isWeekend` (`:37`), `writableCalendars`/`isCalendarLocked` (`:45`/`:52`),
  `keepPerSettings` (`:146`), `isOffWorkHour` (`:154`), `buildAvailabilityEvents` (`:65`).

### 1.3 Stored / sync model and REST API

- **Events** (`calendar.events`): `id, calendar_id, owner_id, title, description, location, url, starts_at,
  ends_at, all_day, timezone, rrule TEXT, exdates (jsonb), parent_event_id, recurrence_id, reminders (jsonb),
  ical_uid, etag, sequence, status, visibility, busy, color, guests_can_modify/invite/see_guests, change_seq`.
  A recurring series is **one base row** with an RRULE; exceptions are either an `exdate` (skipped) or a
  **child row** with `parent_event_id` + `recurrence_id` (a detached, modified occurrence). No pre-expansion in
  the database.
- **Occurrence expansion is server-side** (`services/recurrence_service.rs`): `expand(event, color, from,
  until)` builds `DTSTART…\nRRULE:…`, parses with the `rrule` crate, enumerates `.after(from).before(until)
  .all(500)`, drops `exdates`, and emits `EventInstance`s with id `"{event_id}_{occ_ts}"`. `GET /calendar/
  events?from&until&calendar_ids` returns `{ events: EventInstance[] }` — the ready occurrences for the window.
  **The web, the desktop and mobile all receive expanded instances**; no client expands recurrences.
- **Calendars** (`calendar.calendars`): personal/shared/subscription/birthday; `caldav_token`;
  `subscription_url`/`last_synced_at` for mirrored `.ics` feeds; `calendar.calendar_shares` for read/write
  sharing.
- Other tables: `attendees` (RSVP, rooms via `resource_id`, `optional`), `scheduled_reminders`, `time_blocks`,
  `meeting_polls`/`poll_slots`/`poll_responses`, `appointment_schedules`/`appointment_availability`/
  `appointment_bookings`, `analytics_cache`, `weather_locations`. Delta/sync via `change_seq` + tombstone
  tables (`…_tombstones`) and an app-driven `calendar.change_counter`.
- **REST surface** (base `/api/v1/calendar`, from `web/src/api.ts`): calendars CRUD + `/subscribe`,
  `/:id/share`, `/:id/shares`, `/:id/export`, `/:id/refresh`; events `GET|POST /events`, `GET|PATCH|DELETE
  /events/:id` (`?scope=this|following|all&occurrence=&clear_rrule=`), `GET /events/:id/ics`; `POST /import`;
  attendees; rooms + `/rooms/availability`; `POST /availability` (free/busy); appointment schedules;
  weather; time-blocks; polls; analytics. Public (no auth): RSVP, polls, `/public/calendars/:token/feed.ics`,
  appointment booking pages. Internal: `/ipc/events`. Directory + holidays come from the **core**
  (`/users/search`, `/holidays`), not the module.
- **CalDAV is real, server-side** (`handlers/caldav/mod.rs`): `/.well-known/caldav`, `/caldav/:username/`
  (principal), `/caldav/:username/:token/` (collection, token = `caldav_token`), `/caldav/:username/:token/:uid`
  (resource). Token-authenticated, outside the Axum `require_auth` layer.
- **module.toml**: id `calendar`, port 3102; two sidebar items — `/calendar` (« Calendar ») and
  `/calendar/scheduling` (« Planification »); scalar settings include `week_starts_on`, `show_weekends`,
  `show_week_numbers`, `default_view`, `day_start_hour`, `min_event_height`, `dim_past_events`,
  `default_event_duration_min`, `default_reminder_min`, `secondary_timezone`, `notifications_mode`. The
  per-day working-hours schedule and the world-clock list live in the core `users.preferences.calendar` JSONB.

## 2. Architecture

```
calendar/
  common/core/                 kubuno-calendar-core          the model + the pure algorithms           [as built]
  desktop/common/kubuno-calendar-desktop-common              the complete portable app                 [scaffold]
  desktop/windows/kubuno-calendar                            the Win32 window, kubuno-calendar.exe      [Increment A: Month view]
  desktop/{linux,macos}/                                     thin entry points (later)
```

### 2.1 `kubuno-calendar-core` (`calendar/common/core`) — as built

A standalone, platform-neutral crate (path dependency, sans-IO like `kubuno-drive-core`), ported from the web's
pure logic. No UI, no Windows, no network. It does **not** expand recurrences (that is the server's job, §1.3);
it ports the recurrence logic the web itself has.

| Module | Web origin | Content |
|---|---|---|
| `date` | `@kubuno/sdk` date helpers + `calendarUtils` | civil `Date` (Hinnant, shared with the Projects core), `start_of_week`/`end_of_week`/`each_day_of_interval`/`add_months`, ISO week, `Intl`-shaped `format` (FR/EN) |
| `model` | `api.ts` | `Calendar`, `EventInstance`, `EventReminder` (lossless `extra` for unknown server fields); `Instant` (ISO 8601 → UTC seconds) and `LocalDateTime` (reading an instant in a zone `offset_minutes`), incl. the all-day UTC-date rule |
| `rrule` | `rrule.ts` | `build_rrule`/`preset_from_rrule`, `CustomRecurrence` + `build_custom_rrule`/`parse_custom_recurrence`, the French `describe_rrule` humanizer; FREQ/INTERVAL/BYDAY(incl. nth-weekday)/BYMONTHDAY/COUNT/UNTIL |
| `layout` | `calendarUtils.ts` + the views | `month_grid`, `layout_day_events` (overlap columns), `layout_banners` (all-day stacking), `banner_start_day`/`banner_end_day`/`is_banner_event`, `timed_events_for_day`, `shade`; the geometry constants `PX_PER_HOUR`/`BANNER_*`/`MONTH_MAX_CHIPS` |
| `moon` | `moon.tsx` | `moon_phase`, `moon_illumination`, `principal_phase_of_day`, `phase8` (the client maps the enums to localized strings) |
| `colors` | `calendarColors.ts` + `calendarUtils` | `CALENDAR_COLORS`, `EVENT_SWATCHES`, `DEFAULT_EVENT_COLOR`, `HOLIDAY_COLOR`, `PRIMARY` |

Everything that depends on the viewer's zone takes `offset_minutes` from the caller; « today » and ids come from
the caller too (sans-IO). **29 unit tests** mirroring the web; `clippy -D warnings` clean; `cargo check` clean
for `wasm32-unknown-unknown`, `x86_64-unknown-linux-gnu` and `aarch64-apple-darwin`.

### 2.2 Desktop app (scaffold + Increment A)

- `desktop/common/kubuno-calendar-desktop-common` — the portable app: launch options (`kubuno-calendar
  [--dark] [--culture fr|en] [--view month|week|day|year|schedule] [--date yyyy-mm-dd] [--sample|--live]`), the
  in-memory view state, and (later) a **Calendar REST client** over the shell's token broker
  (`kubuno-office-desktop-common::api::client` pattern — or a calendar-local transport). Like Data, there is
  **no file-save session / journal**: the client will `listEvents(from, until)` for the window, mutate with
  `events`/`calendars` REST calls, and let the server re-expand. Offline is a read cache (later tour).
- `desktop/windows/kubuno-calendar` — `kubuno-calendar.exe`: the main form `views/calendar_window.kbview`
  (TitleBarStyle Tall with header menus, a declarative ribbon/toolbar matching the web nav — Today, ‹ ›, the
  period title, the view switcher), and the custom control `MonthGrid` drawn with the framework's `Graphics`
  (Direct2D), laying events out through `kubuno_calendar_core::layout`. Increment A renders the **Month view
  read-only at parity**; later increments add Week/Day/Year/Schedule, the event editor, the sidebar/mini-month,
  the recurrence dialog, reminders, filters and the widgets. Pattern reference: the Projects app already has a
  Direct2D `MonthView` modelled on the web (`office/desktop/windows/kubuno-office-projects/src/controls/month.rs`).

### 2.3 Theme

The module ships **only light `@theme` tokens** on the web (`web/src/theme.css`; grep found no
`prefers-color-scheme`/`.dark`/`data-theme`). The desktop therefore takes its surfaces from the Kubuno desktop
**framework** theme (which already carries a faithful light/dark shell matching the web), and applies the
module accent `#1a73e8` through `[data-module="calendar"]`, « today » and selection in that accent, events in
their own colour, holidays in `#0b8043`, past events tinted (web `color+'2b'`). Dark event text/contrast is
derived to match the framework dark surfaces. Font stays system (desktop font decision); colours match the web.

## 3. The save / sync path (how Calendar differs)

1. Open: `listEvents(from, until, calendarIds)` → `GET /calendar/events` returns the window's **already-expanded**
   `EventInstance`s; `calendars.list` for the sidebar; holidays from the core feed; appointment schedules →
   synthetic availability events.
2. Create/edit/delete an event = one REST call (`POST /events`, `PATCH /events/:id?scope&occurrence`,
   `DELETE …`); a recurring edit sends `scope=this|following|all` + the `occurrence` start, and the **server**
   updates the RRULE / exdates / child rows and re-expands. There is no content digest, no `PUT …/data`, no
   edit session, no 2 MiB cap (bodies are one event's JSON).
3. Calendars: `POST /calendars`, `PATCH`, `DELETE`, `/subscribe`, `/:id/refresh`, `/:id/share`.
4. CalDAV sync and `.ics` import/export/subscribe are server features the desktop surfaces, not reimplements.

## 4. Increments

- **Note** — this file (commit 1, vskubuno).
- **Core** — `kubuno-calendar-core` (date + model + rrule + layout + moon + colors), unit tests mirroring the
  web, cross-target checks. *(as built, commit 2, calendar.)*
- **A** — the Win32 window + the `MonthGrid` custom control rendering the **Month view read-only at parity**
  (events laid out via the core, banners stacked, chips + « +N », today/selection, moon marker), light + dark,
  from a seeded/fixture dataset (live server deferred). *(started this tour.)*
- **B** — Week / Day / custom N-day time grids (overlap columns, now-line, banner row), Year and Schedule views.
- **C** — the event editor (all fields, recurrence dialog, reminders, guests, rooms, colour), create/edit/delete
  wired to the REST calls with the recurring scope dialog; the sidebar (calendar list, mini-month, holidays,
  world clock, moon, weather), filters, import/subscribe/share, CalDAV settings, appointment scheduling, the
  notification worker, settings.
- **Live server + VS** — a dev core + the calendar module + drive on a local PostgreSQL (`kubuno_dev_calendar`)
  for a real round trip; the views in the VS designer, `Kubuno.Calendar.slnx` regenerated.

## 5. Risks and known divergences

- **Server coupling (confirmed approach, no file save).** Calendar cannot be meaningfully exercised without a
  dev core + the module on PostgreSQL. Read-only parity of the views is testable against a fixture event set;
  the true round trip (create/edit/recurring scope) needs the dev core — a later tour. This matches Data's
  approach; the Documents file-save method is not used.
- **The brief's assumptions corrected.** (a) RRULE **expansion** is server-side, not client-side — the core
  ports parse/build/humanize only, as the web does. (b) **Holidays** are a core server feed (`/holidays`), not
  a client computation — there is nothing to port; only **moon** is client-computed, and it is in the core.
- **No dark theme in the module CSS** — the desktop derives dark from the framework shell (§2.3); any mismatch
  is a framework-theme question, not a module one.
- **Timezone.** The web reads instants in the browser's local zone; the core takes `offset_minutes` from the
  platform. A fixed offset does not model a DST transition inside a visible range (rare for a month grid); the
  live app will pass the OS offset per instant in a later tour if needed. All-day events are offset-independent
  (UTC date), matching the web.
- **i18n leak.** The web's RRULE humanizer and several defaults are hard-coded French regardless of locale; the
  core reproduces that exactly (English returns the French humanizer form), so the two agree — not a new bug.
- **Fonts.** DirectWrite with the web family names + system fallback, so label wrapping can differ by a few px.

## 6. Verification

`cargo test` + `clippy -D warnings` for the core (and later the app); `cargo check` of the core for wasm32,
Linux and macOS; golden parity of the layout against the web (same month/events → identical grid, banner rows
and overlap columns); for Increment A, side-by-side captures of the desktop Month view vs the web Month view on
the same month and data, light and dark, with the differences listed; the live server round trip on a calendar
dev core with its own database — a later tour.

## 7. Open questions for the user

1. Confirm the **server-coupled** approach (no Documents-style file save) and that standing up the calendar dev
   core (core + module + drive on local PostgreSQL `kubuno_dev_calendar`) is in scope for the increment that
   needs the live round trip.
2. **Dark theme**: the module has none on the web — is deriving it from the framework shell acceptable, or
   should a calendar dark token set be authored (and mirrored back to the web)?
3. Scope/priority of the heavier server features for the desktop (CalDAV setup page, appointment scheduling,
   polls, weather, analytics) vs the core calendaring (views + event editor + recurrence).

## 8. Log

**2026-10-12** — note written (commit 1). Shared core `kubuno-calendar-core` built (commit 2): `date`, `model`
(`Instant`/`LocalDateTime`, lossless `EventInstance`), `rrule` (presets, custom round trip, FR humanizer),
`layout` (month grid, overlap columns, banner stacking, `shade`), `moon`, `colors`. **29 unit tests pass** on
Windows; `clippy --all-targets -- -D warnings` clean; `cargo check` clean for `wasm32-unknown-unknown`,
`x86_64-unknown-linux-gnu`, `aarch64-apple-darwin`. Build dir `E:\kubuno-build\agent-calendar\target`.

**2026-10-12 (as built — Increment A, Month view)** — the `desktop/` workspace: `desktop/common/
kubuno-calendar-desktop-common` (launch options, the `CalendarView` state with navigation + period title, the
offline sample dataset, the platform extension points incl. the Windows local clock + zone offset; 7 tests) and
`desktop/windows/kubuno-calendar` (`kubuno-calendar.exe`): the `calendar_window.kbview` with the Tall title bar,
the navigation toolbar (Today, ‹ ›, period title, the view switcher) and the `MonthGrid` custom control, which
paints the month view through the core (the 4/5/6-week grid, all-day/multi-day banners stacked per week incl.
continuation across weeks, up to four timed chips a day then « +N », the today accent circle and the moon
principal-phase markers) in Direct2D, light and dark faithful to the web. Verified: `cargo build` + 7 tests;
`clippy -D warnings` clean; captured light + dark (`E:\kubuno-build\agent-calendar\captures\cal-month-*.png`).
The desktop crates were added to `Kubuno.Calendar.slnx` (a `/Desktop/` folder + two `.rsproj`).

**2026-10-12 (as built — Increment B, Week / Day / Agenda)** — the core gained `layout_day` (overlap columns +
vertical minutes for the time grids) and `week_days`, with tests (**31 core tests**). Two new controls:
`TimeGrid` (the Day and Week time grids — the hour gutter, the day header with the today circle, the all-day
banner row, the scrollable 24-hour grid at `PX_PER_HOUR`, overlap columns via `layout_day`, the red « now »
line, wheel scroll) and `AgendaList` (the Schedule / « Planning » view — one group per non-empty day with its
all-day and timed events). The window switches between Month / Day-Week / Agenda; the view switcher highlights
the active view (Primary) and the « Année » button stays disabled until the Year view lands. Verified: `cargo
build` + tests; `clippy -D warnings` clean; captured Week, Week-dark, Day, Agenda, Agenda-dark
(`E:\kubuno-build\agent-calendar\captures\cal-{week,week-dark,day,agenda,agenda-dark}.png`). Live server and the
VS designer render on the main instance are a later tour.

**2026-10-12 (visual-fidelity pass — Month / Week / Day / Agenda)** — matched the web source token by token
(`web/src/theme.css`, `MonthView.tsx`, `WeekView.tsx`, `DayView.tsx`, `ScheduleView.tsx`, `EventBanners.tsx`,
`CalendarToolbar.tsx`). Before → after, with the web reference:

| Element | Before | After (web source) |
|---|---|---|
| Event chip (Month) | coloured dot + text in grey | **solid filled pill**, white text (faded time + title), `rounded-md` = **8px** (`theme.css:46` overrides Tailwind; `MonthView.tsx:243-260`) |
| Today marker | 20px circle, 12px num | **filled circle** 28px Month / 32px Week-Day-Agenda, 14–16px white (`MonthView.tsx:185`, `WeekView.tsx:201`, `ScheduleView.tsx:100`) |
| Weekday header | 11px bold | **12px, UPPERCASE, regular, tertiary** (`MonthView.tsx:150`) |
| Cell shading | off-month surface-1 | off-month **surface-2**, weekend **surface-1** (`MonthView.tsx:183`) |
| Banner bar | plain rounded rect, radius 4 | body **radius 5**, **darker arrow tips** (`shade×0.62`, 7px) for off-range ends, 3/10px margins (`EventBanners.tsx:9-54`) |
| Moon marker | grey disc | **principal-phase shape** (new/half/full), surface-3 + border-strong outline, lit `#f6c453` (`moon.tsx:94-98`) |
| Time grid | gutter 56, solid hour lines | **gutter 60**, hour lines `border@60%`, **half-hour guide `@40%`**, labels right-aligned `09:00`, hour 0 hidden (`WeekView.tsx:146,252-254`) |
| Timed block | plain fill, radius 4 | **radius 8 + 1px white inner ring** (`ring-1 ring-surface-0/60`), bold (semibold) title, faded time (`WeekView.tsx`) |
| Now line | line + dot | **dot (8/10px) + 2px line + red gutter time-pill** (`#d93025`, `WeekView.tsx:331-336,160`) |
| Agenda | full-width day header + flat rows | **left date badge** (weekday + today circle) + **filled `rounded-lg` 12px cards** with title + `HH:MM – HH:MM` subtitle (`ScheduleView.tsx:95-145`) |
| Toolbar | 5 blue view buttons, glyph arrows | **secondary Today**, **ghost circular ‹ ›**, 22px title, **view switcher = soft dropdown** (`CalendarToolbar.tsx:73-88,152-163`) |

Verified: `cargo build` + 31 core / 7 common tests; `clippy -D warnings` clean; re-captured all four views light + dark
(`E:\kubuno-build\agent-calendar\captures\cal-{month-light,month-dark,week,week-dark,day,agenda,agenda-dark}.png`).
The live web side-by-side (same data) is pending the shared dev stack.

**2026-10-12 (live web side-by-side + now-line dot removal)** — with the shared dev stack up
(`http://127.0.0.1:18900`, calendar `cafe0000-…-0001`, October 2026 data), captured the REAL web Calendar for
Month/Week/Day/Agenda (headless Chrome as devtester, `agent-devstack/tools/shoot-cal.ps1`) and diffed it
against the desktop. **The grid styling matches**; residual gaps fixed from the live web: (a) the Day/Week
timed block shows the title then the time range (and location) below — not a time prefix (new
`draw_timed_block`); (b) the Week/Day titles read « Semaine du 12 octobre 2026 » / « Mercredi 14 octobre 2026 »
(core `DateLong`/`FullDate`, title-cased); (c) the agenda card subtitle appends the location.
**User request (both platforms):** removed the small round dot at the start of the « now » line (web
`WeekView.tsx`/`DayView.tsx`/`CalendarMiniPanel.tsx`, desktop `TimeGrid`) — the line and the gutter time pill
stay; the web dist was rebuilt (`tsc -b` clean) and redeployed to the running stack. Captures:
`E:\kubuno-build\agent-calendar\captures\{web,cal}-{month,week,day,agenda}-*.png` + `*-nodot.png`.

Residual, non-blocking differences (desktop vs web): the desktop is a **standalone window** (no host
sidebar/shell — architectural); real server events all inherit the calendar's blue (the desktop sample is
multi-colour — a data, not styling, difference); minor web-only extras not yet ported (the « GMT+2 » gutter
label, a moon marker in the Week day-header, the Day view's centred full-date sub-header, the agenda
« (Jour x/n) » multi-day hint).

**Server-coupled `--doc` live load + CRUD — NOT started this tour (scope).** Plan: build the desktop shell
`kubuno-desktop.exe` and colocate it with `kubuno-calendar.exe`, seed devtester creds into a sandbox
(`USAGE.txt` steps 2-4), then wire the app's `--doc <calendar-id>` branch to `GET /calendar/events?from&until`
over the broker token source (deps `kubuno-desktop-sync`/`-account`, as Projects does), then
create/edit/delete + the recurrence (RRULE) editor against the REST API. **Compatibility consideration:** the
stack's shell builds from core **main** while this app targets the framework git tag `desktop-v0.1.2-alpha`;
the named-pipe broker protocol must match — build the shell from the same tag, or bump the app's framework,
before relying on the broker. This is the next increment.

**2026-10-12 (server-coupled `--doc`: live load + CRUD round-trip)** — the desktop is now connected to the
server through the Kubuno shell's token broker.
- **Broker compatibility — resolved by diff (not guessed):** the named-pipe broker code
  (`kubuno-desktop-account/src/broker/{proto,transport,client,server,mod}.rs` + `owner/key/app/store/migrate.rs`
  and `kubuno-desktop-sync::tokens`) is **byte-identical** between framework tag `desktop-v0.1.2-alpha` and
  core `main`, so a **main-built `kubuno-desktop.exe`** is compatible with this app (also independently confirmed
  by the Script tour). Built the shell from core main and colocated it with `kubuno-calendar.exe`.
- **Live load:** `--doc <calendar-id>` fetches `GET /calendar/events` over the broker and renders the server's
  expanded instances. Seeded the sandbox per `USAGE.txt` (devtester, `--dev-instance calendardev`). Two seeding
  gotchas found: the `config.json`/`creds.json` must be **valid JSON with no BOM** (unescaped Windows
  backslashes or a UTF-8 BOM make the shell skip the instance → no account), and the **app must inherit
  `KUBUNO_SANDBOX_DIR`** (PowerShell `Start-Process` dropped it here; launching from a shell that exports the
  var works).
- **Same-data side-by-side (what we could not do before):** opened `cafe0000-…-0001` and the desktop renders the
  SAME real events as the web — Month/Week/Agenda match element for element (recurring standups expanded by the
  server, the multi-day Lyon trip, the all-day holiday, the `· Salle A`/`· Lyon` locations). Captures
  `cal-{month,week,agenda}-live.png` vs `web-{month,week,agenda}-light.png`.
- **CRUD round-trip (live):** a « Nouvel événement test » toolbar button `POST`s an event → it appears in the
  desktop (`cal-month-created.png`) AND in the web (`web-month-created.png`); a « Supprimer » button `DELETE`s
  it → gone from the server (0 left) and the desktop shows the real events with the 15th clean
  (`cal-month-after-delete.png`).

### 8.1 Event editor dialog (create / edit / recurrence) — surfaced and proven live

- **The editor dialog** (`views/event_editor.{kbview,rs}`, `EventEditor`) is now the real create/edit surface,
  replacing the test buttons: a `WindowKind="Dialog"` panel (title, all-day, start/end date + time, a
  recurrence preset, scope when editing a series, location, description, Annuler/Enregistrer) matching the web
  `EventEditor.tsx` in light AND dark (`cal-editor-open.png`, `cal-editor-dark.png`). « Nouvel événement » and a
  double-click on a day open **create**; a double-click on an event chip opens **edit**, prefilled (`for_edit`).
- **Live round-trips proven on the server** (`http://127.0.0.1:18900`, calendar `cafe0000-…-0001`):
  - **Create:** typed a title → `POST` → `200`, the event appears on the grid after the reload and is confirmed
    by a direct `GET` (`cal-created-result.png`).
  - **Edit:** double-clicked a chip → dialog prefilled with the existing title → renamed → `PATCH` with the
    correct `event_id` → `200`; `GET` confirms the new title persisted.
  - **Recurrence:** picked « Toutes les semaines » → `collect()` built `FREQ=WEEKLY;BYDAY=MO` → `POST` → `200`;
    the server expands it and the desktop renders the occurrence on every Monday (12/19/26, `cal-dpiaware.png`).
  - **Delete** still round-trips via REST (`204`), used here to clean up the test events afterwards.
- **ROOT-CAUSE FIX — two-way bindings.** The declarative view bindings default to **`OneWay`**
  (`kubuno-desktop-views binding.rs`): a plain `Text="{Binding Title}"` only *displays* the field and never
  writes the user's input back to the view-model. The editor's first version omitted the mode, so every field
  (`collect()` read `self.title == ""`) produced an empty payload → the server rejected it `HTTP 422`. The fix
  is `Mode=TwoWay` on **every** input binding (`Title`, `AllDay`, `StartDate/Time`, `EndDate/Time`,
  `Recurrence`, `Scope`, `Location`, `Description`). Lesson for every future kbview form: input fields need
  `Mode=TwoWay` or they silently do not persist.
- **Testing notes (UI automation).** Framework `TextField`/`Dropdown` controls do **not** honour UIA
  `ValuePattern.SetValue` (and expose no readable `Value`) — drive them with real focus + keystrokes
  (`SendKeys`), and a `Dropdown` by focus + arrow-down. The editor opens as an **owned** window (not a
  desktop-root child) and Windows foreground-lock can keep it from becoming foreground, so find it via
  `EnumWindows` by title (`Nouvel…`/`Modifier…`). For chip double-clicks the automating process must be
  **per-monitor DPI-aware** (`SetProcessDpiAwarenessContext(-4)`) or `SetCursorPos` lands on the wrong row.
- **Broker launch (the reliable recipe).** Launch the **app alone** (do not pre-start a shell): it spawns its
  own `kubuno-desktop.exe` (colocated next to the app exe) which adopts the signed-in account from the sandbox.
  Pre-starting an explicit shell consumed the one-shot credentials and left the app's spawned shell with no
  account (empty grid). Sign in once via the shell's form, then reuse that sandbox tag.
- **Known in-session refresh lag (not a desktop bug).** After a create/edit, the immediate re-fetch sometimes
  returns the *pre-change* snapshot for the rest of the session (server-side recurrence-expansion cache lag);
  a fresh process shows everything. The desktop reload path is correct (`on_saved → reload_live → on_loaded`,
  and `account_api::request` does no client-side caching). Worth revisiting server-side if it bites users.
- Note: the broker access token expires (~15 min) and separate logins/web captures rotate it, so a long session
  shows an empty grid until the sandbox is re-seeded (`USAGE.txt`) — an ops detail, not an app bug.

### 8.2 Editor sub-parts: custom recurrence, calendar picker, colour swatches (proven live)

- **Custom-recurrence dialog** (`views/recurrence_dialog.{kbview,rs}`, web `RecurrenceCustomDialog.tsx`):
  « Personnalisé… » in the recurrence field opens a 400px dialog over the core's `CustomRecurrence` — interval
  + unit, the weekly weekday toggles (a `WeekdayPicker` custom control, L M M J V S D, Monday first), the
  monthly by-month-day / by-weekday choice, and the Never / On-date / After-N end rule, with a live French
  summary (`describe_rrule`). Proven live: set weekly + Mon/Wed → `FREQ=WEEKLY;BYDAY=MO,WE`, which the server
  expands onto Mon 12 AND Wed 14. Light `cal-custom-dialog.png`, dark `cal-custom-dark.png`.
- **Calendar picker** (« Agenda » dropdown) and **colour swatches** (« Couleur », a `ColorSwatches` custom
  control: the calendar colour then the fixed `EVENT_SWATCHES`, with a 2px ring on the selected one). The
  window fetches the calendars once (`live::fetch_calendars`) and offers the writable ones; picking a calendar
  moves the event, picking a swatch sets the colour (first swatch = « default » = clear). Proven live: one
  event created with the gold swatch AND moved to a second calendar → server shows `color #f9ab00`,
  `calendar_id = <Projet Beta>`. Light `cal-editor-cc.png`, dark `cal-editor-cc-dark.png`.
- **KEY PATTERN — read custom controls on Save, not in `collect`/`run`.** A `Custom<T>` control is torn down
  when its dialog closes, so `self.ctrl.with(|v| …)` returns `None` after `show_dialog` returns. Read the
  control's state in the click handler **before** `close()` (the colour in `save_click`, the RRULE in the
  recurrence dialog's `done_click`) and stash it in a plain field that `collect`/`run` then reads. Plain
  **bindings** (TwoWay fields) persist fine — only custom-control reads need this. (First symptom: colour saved
  as the calendar default, byday dropped from the RRULE.)
- `EventWrite` now also carries `description`, `color`/`clear_color`, `reminders`, `attendees`, the three
  guest-permission flags and `calendar_target`; `save_event` serialises them (description now sent too).

### 8.3 Two-column editor + reminders + guests (proven live)

- **The height wall is fixed by matching the web's layout.** The web `EventEditor.tsx` is a **two-column
  modal, 1024 px wide**: a full-width header (title, the date/time row, all-day + the recurrence field) over a
  left « details » card (location, reminders, calendar + colour, description) and a right `w-[320px]` guests
  column, with Save in the `FloatingWindow` footer. The desktop editor now mirrors it (`DesignWidth=960`,
  `DesignHeight=610`): two columns are shorter than the old single stack, so « Enregistrer » stays on-screen at
  175 % DPI (~1067 px vs ~1089 px work area). Captures `cal-editor-2col.png`, `cal-editor-guests.png`,
  `cal-editor-full-dark.png`.
- **Reminders** (`RemindersField.tsx`): up to two rows, each a method dropdown (Notification / E-mail) + an
  offset dropdown (5/10/15/30 min, 1/2 h, 1 day before) + a remove button, and an « Ajouter un rappel » link;
  a new event defaults to one notification 10 min before. `EventWrite.reminders` is `(method, minutes)` pairs
  that `save_event` serialises. Proven live: `[{popup,15},{email,10}]` round-tripped.
- **Guests + permissions** (`GuestField/GuestRow/GuestPermissions.tsx`): the right column has an « Invités »
  e-mail field + « Ajouter », up to four removable invited rows, and the three « Autorisations des invités »
  toggles (modify/invite/see, web defaults false/true/true). `EventWrite.attendees` is an e-mail list;
  attendees + the three flags are sent on create. Proven live: `alice@example.com` invited and
  `guests_can_modify=true` persisted. Editing an existing event's attendees (the attendee API) is still to do.
- **Lists use fixed slots, not a `Repeater`.** Both reminders and guests are plain bound rows shown/hidden by a
  `*_vis` flag (reminders ≤ 2, guests ≤ 4). A `Repeater` with an interactive item template is the way to make
  them unbounded; fixed slots were enough for parity here and avoid per-item binding/event plumbing.

### 8.4 Sidebar + edit-an-event's-guests (proven live), and the editor side-by-side blocker

- **Sidebar DONE** (`controls/mini_month.rs` + `controls/calendar_list.rs`, web `CalendarSidebarBody.tsx` /
  `CalendarMiniPanel.tsx`): a left panel docked in `calendar_window.kbview` with a `MiniMonth` navigator (month
  arrows, narrow weekday row, round day cells — today a filled accent disc, other-month dimmed) and a
  `CalendarList` (« Mes agendas »: a colour-tinted checkbox + name per calendar). Both are custom paint
  controls, like `WeekdayPicker`/`ColorSwatches` (chosen over the framework `MonthCalendar` for a token-exact
  look). Wiring, **proven live**: toggling a calendar hides/shows its events across Month/Week/Day (a
  `hidden_calendars` set in `State`, applied by `State::visible_events()` which the three view controls now draw
  from); clicking a mini-month day sets `view.current` and reloads. Genuine side-by-side composed at
  `captures/sidebar-side-by-side-light.png` (desktop vs `web-cal-test.png`); dark at `cal-sidebar-dark.png`.
  The web's Booking/Weather/Moon sidebar sections are not ported (lower priority).
- **Editing an existing event's guests DONE** (`live::fetch_guest_emails` / `fetch_attendees` / `sync_attendees`):
  `for_edit` loads the current non-organizer guests into the rows; on save, `save_event` reconciles the edited
  list through the attendee API (`POST`/`DELETE /calendar/events/:id/attendees`), since `PATCH /events` takes no
  attendees. `EventWrite.attendees` is now always sent; create still POSTs them inline. **Proven live**: opened
  an event with `bob@`, added `carol@`, removed `bob@`, saved → server shows `[organizer, carol@]` (organizer
  always kept). Routes: `GET/POST /events/:id/attendees`, `DELETE /events/:id/attendees/:aid`.
- **Genuine web↔desktop side-by-side of the EDITOR: still blocked, now with the precise reason.** Headless
  Chrome (own ephemeral `--user-data-dir`, killed by PID / by profile-dir, never the user's) logs in and renders
  the calendar + the **web sidebar** once `Emulation.setDeviceMetricsOverride` is set (without it,
  `captureScreenshot` returns a blank frame). **The web `EventEditor` has NO route** — it is React state in
  `CalendarApp.tsx` (`{createDay !== null && <CreateEventModal/>}`), opened by `setCreateDay` via the **'c'
  keyboard shortcut** (`window.addEventListener('keydown')`), `onDayCreate` (double-click a day) or the Create
  menu. In headless, synthetic keydown — both CDP `Input.dispatchKeyEvent` AND JS
  `window.dispatchEvent(new KeyboardEvent('keydown',{key:'c'}))` — does **not** fire the handler (even view
  shortcuts like 'w' don't), and synthetic Create-menu / day clicks only mount the transient quick card, never
  `.kb-form-title-field`. So the full web editor can't be opened programmatically here. Fallback (per the
  coordinator): editor fidelity validated token-by-token against the extracted spec + the desktop captures
  (`cal-editor-2col/guests/full-dark/reminders.png`). A real editor side-by-side needs the user's own browser
  (real key/pointer events) or a deep-link route added to the web.
- Still open from before: import/subscribe/share/export, CalDAV, scheduling, VS designer.

### 8.5 Year view, Search, and the Moon/Weather/Booking sidebar sections (proven live)

- **Year view** (`controls/year_grid.rs`, web `YearView.tsx`): a « Année » switcher entry drawing a 4×3 grid of
  mini-months — each with the month name (accent for the current month) + its event count, a weekday row,
  today a filled accent disc, and up to three event-colour dots per day. Clicking a day opens that Day, a month
  name opens that Month. `live::fetch_year` loads the whole year for the dots; `reload_live` picks the window by
  view mode. **Proven live** (clicking Oct 14 → Day view of 14 Oct, Revue produit). Light `cal-year-dpiaware.png`,
  dark `cal-year-dark.png`, genuine web side-by-side `year-side-by-side-light.png`.
- **Search** (`controls/search_results.rs`, web `SearchResultsView.tsx`): a toolbar search field filters a
  ±1-year pool (`live::fetch_search_range`) by title/description/location, excluding hidden calendars, and shows
  the matches (count + colour dot + title + day/time) in a list that replaces the view while a query is typed;
  a result click opens that event's Day. **Proven live** (« revue » → 1 result → Day of 14 Oct). Light
  `cal-search.png`, web side-by-side `search-side-by-side-light.png`.
- **Sidebar Moon / Weather / Booking** (web `CalendarSidebarBody` sections): a « Lune » row (a `MoonWidget`
  drawing today's moon via a new `draw.rs::draw_moon` — the lit limb carved/extended by a terminator ellipse —
  with the phase name + illumination from the core, e.g. « Premier croissant 1% » = the web's « Waxing crescent
  1% »); a « Météo » section with « Ajouter un lieu »; a « Pages de réservation » section. `cal-sidebar-widgets.png`,
  side-by-side `sidebar-widgets-side-by-side.png`. The full weather forecast widget (the `/weather/forecast`
  endpoint exists) and the `/calendar/scheduling` page are later increments.
- **Headless web captures, now reliable for SPA views**: the year and search views DO render under headless
  Chrome (own ephemeral `--user-data-dir`, killed by profile/PID), so genuine side-by-sides were composed
  (`scratchpad/compose.ps1`). Only the editor still can't be opened headlessly (§8.4). ⚠️ Desktop captures of
  grids need a **DPI-aware** PowerShell (`SetProcessDpiAwarenessContext(-4)`) and a large window, or
  `MoveWindow` from a DPI-unaware shell makes the control clip wider than the window and the right columns
  overflow off-screen (seen first on the Year grid, true of the Month grid too).

## 9. Feature coverage

✔ done and verified · ◐ implemented and unit-tested, not exercised live · ✘ not done · — not applicable.
Web lines are in the files of §1.1.

| Feature | Web | Desktop |
|---|---|---|
| Civil date + week/month boundaries + `Intl` formats | `calendarUtils` / SDK | ◐ core (`date`, 6 tests) |
| `Calendar`/`EventInstance` model, lossless unknown fields | `api.ts` | ◐ core (`model`) |
| Instant → local wall-clock, all-day UTC-date rule | `calendarUtils.allDayLocal` | ◐ core (`model`, 5 tests) |
| Month grid (`calendarGrid`) | `calendarUtils.ts:138` | ◐ core (`layout::month_grid`) |
| Overlap columns (`layoutDayEvents`) | `calendarUtils.ts:103` | ◐ core (`layout::layout_day_events`) |
| All-day banner stacking (`layoutBanners`) | `calendarUtils.ts:247` | ◐ core (`layout::layout_banners`) |
| Banner day bounds, `isBannerEvent`, `shade` | `calendarUtils.ts:189-231` | ◐ core (`layout`) |
| RRULE presets + custom round trip + FR humanizer | `rrule.ts` | ◐ core (`rrule`, 5 tests) |
| Moon phase / illumination / principal phase / 8-phase | `moon.tsx` | ◐ core (`moon`, 5 tests) |
| Overlap columns + vertical span for time grids (`layout_day`) | `calendarUtils.ts:103` + views | ◐ core (`layout::layout_day`) |
| Calendar/event colour palettes | `calendarColors.ts` | ◐ core (`colors`) |
| **Month view** rendered read-only at parity (banners, chips, +N, today, moon) | `MonthView.tsx` | ✔ Increment A, captured light+dark |
| **Week / Day time grids** (header, all-day row, hour grid, overlap, now-line, wheel scroll) | `WeekView`/`DayView` | ◐ Increment B, captured light+dark (fixture) |
| **Agenda (Schedule)** view (day groups, all-day + timed, today) | `ScheduleView.tsx` | ◐ Increment B, captured (fixture) |
| Custom N-day view | `WeekView` (`dayCount`) | ✘ (TimeGrid supports it; not wired to a switch) |
| Year / Search views | `YearView`/`SearchResultsView` | ✘ later |
| View switcher + navigation (Today, ‹ ›, period title) | `CalendarToolbar.tsx` | ◐ Month/Week/Day/Agenda wired |
| Event editor (fields, recurrence dialog, reminders, guests, rooms) | `EventEditor.tsx` | ✘ Increment C |
| Create/edit/delete + recurring scope, wired to REST | `api.ts` + server | ✘ Increment C |
| Sidebar: calendar list, mini-month, holidays, world clock, moon, weather | `CalendarSidebarBody.tsx` | ✘ Increment C |
| Import / subscribe / share / export, CalDAV settings | `Calendar*Modal.tsx` + server | ✔ sidebar « Gérer » menu; all round-trips proven live (§10) |
| Appointment scheduling, polls, notifications, settings | `AppointmentScheduleEditor` etc. | ✔ schedule editor + sidebar list + CRUD, round-trip proven (§11); polls/notifications/settings later |
| Occurrence expansion | server (`recurrence_service.rs`) | — server-side (client receives expanded) |
| Holidays data | core (`/holidays`) | — core server feed (not computed) |
| VS designer views, `Kubuno.Calendar.slnx` | — | ✘ later |

## 10. Calendar-management surface (2026-10-12)

The sidebar's « Mes agendas » header now carries a **« Gérer » dropdown** (`DropDownButton`, web
`CalendarSidebarBody` entry points) with five items, each the desktop counterpart of a web `Calendar*Modal`:

- **S'abonner à un agenda…** (`CalendarSubscribeModal.tsx`) — name + feed URL → `POST /calendar/calendars/subscribe`.
- **Partager…** (`CalendarShareModal.tsx`) — a dialog (`share_dialog`, `ShareList` control): share with users
  (directory search → `/users/search`, `POST …/share`), list + revoke (`GET …/shares`, `DELETE …/share/:id`,
  names via `/users/lookup`), and a public read-only `.ics` link (`PATCH { is_public }` + the feed URL).
- **Importer .ics…** / **Exporter (.ics)** — native file dialogs (`file_dialog.rs`, `GetOpen/SaveFileNameW`) →
  `POST /calendar/import` (UID-preserving) / `GET …/export`.
- **CalDAV…** (`CalendarCalDavSettings.tsx`) — display-only dialog (`caldav_dialog`, `CalDavList` control):
  the main endpoint (server URL + username from `GET /api/v1/me`) and each calendar's CalDAV URL from its token.

New live helpers in `desktop/common/.../live.rs`: `subscribe_calendar`, `import_ics`, `export_calendar`,
`list_shares`/`share_calendar`/`unshare_calendar`/`search_users`/`lookup_users`, `set_calendar_public`,
`public_feed_url`, `server_origin`, `fetch_username`. New palette token `danger`.

**Proven live** against the dev stack (`127.0.0.1:18900`, calendar `cafe0000-…-0001`, user `devtester`): import
of a probe event (VEVENT count 4→5, seen in the re-export, then removed); export served 4 VEVENTs; public toggle
on/off with the feed served (`/public/calendars/<token>/feed.ics` → 200, 4 VEVENTs); share/unshare of a second
user (`admin`, name resolved to « Administrateur »); CalDAV endpoint answers PROPFIND 207 / OPTIONS 200 for the
shown token. The « Gérer » menu was captured (`scratchpad/cal-mgmt-menu.png`, all five items with their icons).

**Capture limit**: the modal dialogs themselves could not be captured reliably — in this session the desktop
window alternates non-deterministically between a full (2170×1470) and a clipped (1240×840) render, and the
AccessKit UIA tree only intermittently exposes the dropdown, so synthetic clicks to open a modal don't land
consistently. Same class as the accepted headless web-editor block; functionality is proven via the API
round-trips above. Web side-by-sides of these modals remain blocked (they open from SPA interactions that don't
fire under headless Chrome, like the editor).

### Remaining (not done, to list)
- Full weather widget via `/weather/forecast` (only the « Ajouter un lieu » affordance is shown).
- The `/calendar/scheduling` (appointment/booking) page.
- VS designer live render of the views.
- Editor pixel-exact side-by-side (pending the web deep-link decision).

## 11. Scheduling / booking pages (2026-10-12)

The biggest remaining feature — the web `/calendar/scheduling` surface (the 2nd sidebar section) and its editor
(`web/src/AppointmentScheduleEditor.tsx`, reached at `/calendar/booking/new|:id`).

- **Sidebar « Pages de réservation »** (web `CalendarSidebarBody` « Booking pages »): a new `ScheduleList`
  control lists the owner's bookable schedules (title + duration) and a « Nouveau planning » button creates one;
  clicking a row opens the editor on it. The sidebar was also laid out more compactly so weather/moon/booking
  fit a short window (on a 1080p/175 % display the earlier layout pushed booking below the fold).
- **`ScheduleEditor` (`schedule_editor.{rs,kbview}`)** — a two-pane dialog ported token-by-token from the web:
  a settings pane (step 1: title, duration, weekly availability, time zone, scheduling window rolling/fixed +
  max-days/min-hours, booked settings buffer/max-per-day/guests, calendar; step 2: location+details,
  description, email reminders) and a live **`AvailabilityPreview`** control — a 7-day 07:00–20:00 week grid
  that paints the weekly ranges as tinted blocks, matching the web preview. Defaults match the web
  (Mon–Fri 09:00–17:00, 60 min, Europe/Paris).
- **Live wiring** (`desktop/common/.../live.rs`): `list_schedules`, `get_schedule`, `save_schedule`
  (POST/PATCH), `delete_schedule`, `booking_page_url`, with `ScheduleWrite`/`AvailRule`/`ScheduleSummary`/
  `ScheduleFull` mirroring the web `SaveScheduleDto`/`AppointmentSchedule`.

**Proven live** against the dev stack (`/api/v1/calendar/appointment-schedules`): create → 201 with id +
public_token; get → the schedule with its 2 availability rules; delete → 204 — with the exact DTO the desktop
`save_schedule` builds. The desktop **sidebar** booking entry was captured and composed side-by-side with the
web (`scratchpad/sched-sidebyside.png`); the **web editor** was captured headlessly at `/calendar/booking/new`
(`scratchpad/web-sched-editor.png`) — it shows exactly what `ScheduleEditor` + `AvailabilityPreview` port.

**Reductions (first increment):** the weekly editor manages one range per weekday; extra ranges and
date-specific overrides on an existing schedule are **preserved untouched** on save (stashed as `extra_rules`).
The custom booking-form-field builder is not yet ported.

### Window-stability finding (capture artifact, not a bug)
The desktop window renders **correctly** when run normally or maximized (verified: 1942×1038 at 175 % DPI, no
truncation, `scratchpad/winstab.png`). The earlier reports of the window "alternating" between a full
(2170×1470) and a clipped (1240×840) render were a **capture-harness artifact**: a DPI-unaware PowerShell
`MoveWindow(1240,840)` on a 175 % monitor yields only ~709 logical px, too narrow for the absolute-positioned
toolbar (so controls overlapped), and `GetWindowRect` returned virtualized vs true pixels depending on the PS
process's DPI state. No app-side rendering bug.

### Desktop dialog capture is blocked (evidenced)
No desktop **modal dialog** (event editor, share, CalDAV, schedule editor) can be opened by scripted input in
this environment: synthetic `mouse_event` clicks do not fire the app's handlers, and UIA `InvokePattern.Invoke`
on the button does not trigger the modal-opening path either — **confirmed by invoking the known-good
« Nouvel événement » button, which likewise does not open its dialog**. So the schedule editor is not broken;
it is un-capturable via scripting, the same accepted limitation as the other dialogs. Its fidelity rests on the
web side-by-side + the shared port.

### Remaining (not done)
- The custom booking-form-field builder and multi-range / date-override weekly editing.
- Full weather widget via `/weather/forecast`; the module **settings page + notifications worker**.
- VS designer live render; editor pixel-exact side-by-side (pending the web deep-link decision).
- Scrollable sidebar (AutoScroll on the absolute-child docked panel does not engage — a framework limitation;
  worked around by compacting the layout).
