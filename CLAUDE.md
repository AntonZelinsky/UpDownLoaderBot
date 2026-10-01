# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Telegram bot ([@UpDownLoaderBot](https://t.me/UpDownLoaderBot)) that takes an Instagram
Reels/post/IGTV or TikTok link out of a chat message and sends the video back. .NET 10, ASP.NET Core host
with the bot as a `BackgroundService` on long polling. `readme.md` is the detailed reference —
it documents the *why* behind most of the non-obvious decisions below.

## Commands

```bash
dotnet build                      # solution: UpDownLoaderBot.slnx (src + tests)
dotnet test                       # all tests; no network, no cookies needed
dotnet test --filter "FullyQualifiedName~TelegramVideoPreparerTests"
dotnet test --filter "DisplayName~Reports_display_size"   # single test

export UpDownLoaderBot__Telegram__Token="<token>"
dotnet run --project src          # bot + GET /health (calls Telegram getMe)

docker compose build && docker compose up -d   # local container run
```

External tools must be on PATH for a local run and for the video tests: `brew install yt-dlp ffmpeg`
(`ffprobe` ships with ffmpeg). `readme.md` has a fix for the macOS 26 `No module named expat`
failure of the brew yt-dlp.

## Architecture

`ARCHITECTURE.md` is the map for a human reader: a Mermaid component diagram, a table of what each
class does, a sequence diagram of one request, which downloader takes which link, and a "where to
look" table. **Keep it in step** when a class is added, renamed or moved, or the request flow
changes — it names classes and methods, not line numbers, so only a real change makes it stale. Its
tables are aligned in the source, columns padded with spaces and dashes, so the raw file reads as
well as the rendered one.

Three layers, and the dependencies only ever point one way. `ArchitectureTests` reads the sources
and fails the build if any of this stops being true — it is one assembly, so the compiler has no say:

```
src/Bot/           Telegram: polling, routing, sending, texts  → Core, and Tools for the folder
src/Core/          the model, the contracts, the scenario      → Tools/Ffprobe only
src/Tools/         what drives an external binary, the disk, or a mirror
  ProcessRunner    yt-dlp/ffmpeg/ffprobe, one entry point
  DownloadFolder    a directory per request
  Ffprobe/         the preparer and its wire format            → Core (returns its PreparedVideo)
  Http/            the site-agnostic mirror downloader base    → Core (implements its interfaces)
  YtDlp/           the site-agnostic downloader base           → Core (implements its interfaces)
src/Providers/     one folder per platform: Instagram/, TikTok/ → Core, Tools/Http, Tools/YtDlp
src/Program.cs     composes every layer, plus GET /health
```

Two words decide where a new file goes: **`Tools/` drives an external binary, the disk or a mirror;
`Providers/` is one folder per platform-source.** A third platform is a `Providers/<Name>/` folder
holding an `IPlatformLinks`, its downloaders and one `Add<Name>` extension method; nothing else moves.

Registration lives in `<Name>ServiceCollectionExtensions`, never in `Program.cs`.

- **Only `Bot/` and `Program.cs` may reference `Telegram.Bot`.** Nothing below them knows the chat
  exists.
- **`Core/` names no platform it downloads from.** Adding one stays a matter of `Providers/` and
  `Program.cs`; if "Instagram" or "TikTok" appears under `Core/`, something landed in the wrong layer.
- **`Core/` reaches for exactly one piece of infrastructure**, the preparer in `Tools/Ffprobe/`.
  Everything else there is checked by imports alone, which is why `ProcessRunner` and
  `DownloadFolder` were moved out of the root namespace: a child namespace sees the root *without*
  a `using`, so while they lived there no import could tell whether `Core/` had reached for them.
- **A tool wrapper takes `ProcessRunner` by nesting, not by import.** `Tools.Ffprobe` and
  `Tools.YtDlp` are children of `Tools`, so the same C# rule that spoiled the check above works for
  us here and says "the plumbing belongs to the wrappers".
- **`Bot/` may import `UpDownLoaderBot.Tools`** — the worker creates the request's folder — and that
  import opens `ProcessRunner` too, so the test keeps that one out by name.
- **Each platform registers itself.** `Providers/<Name>/<Name>ServiceCollectionExtensions.cs` takes
  that platform's config section and the startup logger, and `Program.cs` is two calls. The order of
  those calls is behaviour: `MediaLinkParser` asks the platforms in registration order.

Request flow:

1. `TelegramBotWorker.HandleUpdate` only decides what arrived and hands it to a method per kind: a
   `/start` command (`CommandName`) goes to `SendStartCommandInstructionsToChat`, a link to
   `HandleMediaRequest`. A command that is not ours is neither answered nor swallowed — it falls
   through to the link search, because it may belong to another bot in the group and still carry a link.
2. `MediaLinkParser.FirstIn` turns the message text (`update.Message ?? update.ChannelPost`) into a
   `MediaLink(Url, Platform, Id)` by asking every registered `IPlatformLinks` **in registration
   order** — the first platform to recognize anything wins, and a message carrying links of two
   platforms is decided by that order rather than by which link comes first in the text. Arbitrating
   by position needs each match's offset and is left until a second platform exists (`backlog.md` §8).
3. `MediaFetcher.Fetch` walks the `IMediaDownloader`s that take the link, **in registration order**,
   stopping at the first whose files survive preparation.
4. `TelegramVideoPreparer.Prepare` runs `ffprobe` on each file, rejecting anything unsendable.
5. `SendVideo`, as a reply to the link. One request still means one video (`post.Media[0]`); the
   album branch arrives with multi-media.
6. A failure reacts 😴 on the original message (`FailureReaction`; 😵 is not among the reactions the
   Bot API allows a bot). **Nothing to send is not a failure**: a `NothingToSendException` — no
   downloader takes the link, or yt-dlp reached the post and found only photos — ends the request in
   silence. A reaction there would read as the bot judging the post.

Key invariants that are easy to break:

- **A downloader claims only links whose content it can deliver whole** (`CanHandle`). Claiming one
  and answering with a part of it stops the fallback at an incomplete answer, and the downloader that
  would have returned all of it never runs. This is the routing: nothing above dispatches by platform.
  It follows that a downloader must not claim a shape it knows it cannot serve either — the mirror
  declines a TikTok `/embed/` link rather than spending a request to be told 404.
- **The last downloader of a platform is the exception: it claims every link of that platform that
  may hold a video**, shapes it may fail on included, because a failure is owed a 😴 and nothing
  after it gets the chance. What it declines is only a shape that holds no video by definition — the
  TikTok `/photo/` post — and a link nobody takes is *nothing to send*: silence, not a reaction. So
  `TikTokYtDlpDownloader.CanHandle` tests the platform and rules out the photo shape, while the
  mirror in front of it consults the link's shape for more.
- **Whether a post holds a video is learnt from the service, not from the link.** An Instagram `/p/`
  is a photo, a carousel or a video alike, so the intake keeps matching it and yt-dlp is asked.
  `YtDlpFailedException.HoldsNoVideo` — **every** `ERROR:` line is `No video formats found` — is what
  turns that answer into a `NothingToSendException`; a carousel where one item failed on the way
  still had something to download and stays a failure. In `MediaFetcher` it is a hand-over like any
  other (the next downloader still runs), but at the end it outweighs another downloader's failure:
  it is a fact about the post, not about getting to it.
- **Preparation happens inside the downloader loop, not after it.** A file that ffprobe rejects must
  fall through to the next downloader instead of being sent or failing the whole request — the
  criterion is *nothing survived preparation*, which with one file is the same as *the preparer threw*.
  **`--force-overwrites` is part of that**: every downloader names the file after the post, so the
  refused file sits under the name yt-dlp is about to write, and without the flag yt-dlp reuses it and
  reports it as its own success.
  Downloader registration order inside each platform's `ServiceCollectionExtensions` is therefore
  behaviour, not style — among those that take the link.
- **Downloaders return files or throw** — never a "nothing found" result (see `IMediaDownloader`).
  Keep that when a post starts bringing several: an empty success would need handling everywhere a
  throw already is.
- **Nothing deletes its own files.** Every request gets a `DownloadFolder`
  (`downloads/2026-08-21_14-05-33.482/`, the moment it arrived, so a folder that outlives a crash
  says when it happened), and disposing it removes the whole directory — the video and yt-dlp's
  half-written `.part` leftovers alike. Downloaders and the preparer receive that folder as a plain
  string and only write into it; cleanup lives in exactly one `using` in `HandleMediaRequest`, which
  outlives the upload because the file is read while sending. Do not reintroduce per-file deletion.
- **A downloaded file is named after the post** (`link.Id`), the same whichever downloader served it.
  A carousel will need an index added to that, or its items would overwrite each other.

### Downloaders (`src/Providers/`, bases in `src/Tools/Http/` and `src/Tools/YtDlp/`)

A mirror downloader is a rewritten host plus a bot User-Agent, so everything but those lives in
`Tools.Http.MirrorDownloaderBase`: the 60 MB cap read both from `Content-Length` and from the
bytes actually written, the `video/*` check that catches a landing page, the extension and the
naming by `link.Id`. A subclass says which links are its own, where they live on the mirror and
what to call it in a log line.

#### Instagram

- `InstagramLinks` : `IPlatformLinks` — every Instagram link shape in one place, and only what
  recognizing a link needs: what the intake searches for (one pattern, with the post kind and id as
  named groups) and whether a link holds exactly one video. Rewriting the host onto a mirror stays in
  `KkInstagramDownloader`, whose business that is. Registered once and handed out twice — as
  `IPlatformLinks` to the parser and as itself to the mirror downloader — so resolve it, never
  register the interface separately, or there would be two instances. `TikTokLinks` is the same class
  for TikTok, and the two are the only place a platform's link shapes are written down.
- `KkInstagramDownloader` — plain HTTP to a kkinstagram mirror, no auth, no external tooling.
  Rewrites the host and sends a **bot** User-Agent (a browser UA gets an HTML landing page). It
  answers with a single file, so it **takes only `/reel/`, `/reels/` and `/tv/`** — links that hold
  exactly one video. A `/p/` post may be a carousel, of which it would deliver a part, so that goes
  straight to yt-dlp. `EnsureNotStillImage` still matters, though: the mirror can serve a JPEG under
  `video/*` on a reel link too — that is now a rare guard rather than the normal path.
- `InstagramYtDlpDownloader` : `YtDlpDownloaderBase` — the fallback that needs cookies, and takes
  every Instagram link including carousels. The base class is deliberately site-agnostic; anything
  service-specific goes through `AddServiceArguments` so Instagram cookies cannot leak into another
  service's downloads, and `CanHandle` is abstract so a downloader added for another service has to
  say which links are its own.

#### TikTok

- `TikTokLinks` : `IPlatformLinks` — one pattern for every shape, because a link that opens in a
  browser and gets no answer from the bot reads as a bug. Hosts: `tiktok.com`, `tiktokv.com` (share
  links from the app still use it), `www.`/`m.`/`lite.` (TikTok Lite) and `vm.`/`vt.` for short codes.
  Paths: `/@user/{video,photo}/<id>`, `/share/{video,photo}/<id>`, `/t/<code>`, a bare `<code>` on
  `vm.`/`vt.` only, and the two legacy ones `/embed/<id>` and `/v/<id>.html` — **`.html` has to stay
  inside the match**, since without it the URL is one yt-dlp no longer recognizes. A locale-prefixed
  path (`/en/@user/…`) is not a shape: TikTok answers it with a 404. The
  `id` group repeats across the alternatives, which .NET allows — the branch that matched wins. **A
  short link carries no post id**, so its own code stands in; the id is only ever a file name inside
  the request's own folder, so that is enough. `/photo/` is matched on purpose, so that the link is
  recognized and then declined by every downloader — the log says what it was, and the chat gets
  silence rather than a reaction for a post that never held a video.
- `TikTokMirrorDownloader` : `MirrorDownloaderBase` — `d.tnktok.com`, the fxTikTok mirror. **The `d.`
  prefix is what serves the file**; the bare host and `vxtiktok.com` answer with an HTML embed page.
  Like kkinstagram it varies by client, so the bot UA is what gets the file rather than a redirect
  back to tiktok.com. **Nothing but the host is rewritten**, short links included: the mirror answers
  `/<code>` the way it answers `/t/<code>` and follows the redirect itself, so it is the fast path for
  every shape — unlike kkinstagram, which has to pass on a link it might only half deliver. The one
  shapes it declines are the photo post, which holds no video for anything to deliver, and the two
  legacy paths, which it answers with 404.
- `TikTokLinkShape` — what `TikTokLinks.ShapeOf` reports: `Video`, `Photo`, `Short`, `Legacy`,
  `Unknown`. **A fact about the link, not a permission**, which is why it is an enum and not a
  `MayTheMirrorHaveThis` predicate: the policy stays with the downloader that owns it, and a
  downloader added later picks its own subset without `TikTokLinks` growing a predicate for it. The
  boolean it replaced could only answer one consumer's question, and the mirror needed two.
- `TikTokYtDlpDownloader` : `YtDlpDownloaderBase` — the fallback, and it takes every TikTok link but
  a photo post, so a link that may hold a video and cannot be served ends in a 😴 rather than in
  silence. **No cookies**: the extractor solves TikTok's challenge in pure Python (`hashlib`), so no
  JS runtime and no `--cookies` are needed, and nothing is mounted for it.

TikTok specifics worth knowing: the extractor prints `Your IP address is blocked from accessing this
post` whenever the API hands back nothing — for a deleted video as much as for a real block — so
`IsFinal` deliberately does *not* read it as final; a photo post's URL has no extractor at all and
comes back as `Unsupported URL`, which is why nothing takes that shape any more — the run could only
fail.

yt-dlp specifics that look wrong but aren't: `-I 1:10` instead of `--no-playlist` (the Instagram
extractor ignores that flag for carousels, so the range bounds how far in the search goes);
`--max-downloads 1` plus `--ignore-errors`, which together walk a carousel to its first actual
video and stop — a photo on the way raises `No video formats found` and is stepped over; **success
decided by the printed file path, not the exit code** (both of those flags make it exit non-zero,
101 for the download limit, on a perfectly good download); a format selector that prefers a
*progressive* rendition (the DASH ladder is VP9-only, ~70 MB, over the Bot API limit and unplayable
on iOS). The output template names the file after the post — `<link.Id>.%(ext)s`, not yt-dlp's own
`%(id)s`, so the mirror and yt-dlp agree — inside the request's own folder, and yt-dlp reuses an
existing file with the same name, so the folder being fresh per request is what keeps a leftover
from an interrupted run out of the reply.

**`-S` puts the codec before the resolution** (`FormatSort`, `protected virtual`), because the
preparer refuses anything but H.264 outright: a taller rendition in another codec is not a better one
but an unsendable one. TikTok proves it — its 720p is h265-only, so `res` first picked a file every
request would have failed. `TikTokYtDlpDownloader` overrides the tail with `tbr`, as TikTok offers one
resolution at several bitrates and `+size` would settle on the worst.

The retry (`Attempts = 2`, 5s apart) is for a download that failed on the way to the post, not on the
post: `YtDlpFailedException.IsFinal` reads stderr and breaks the loop when every `ERROR:` line is
about the post itself (no video in it, unavailable, private, login required), because a second run
prints the same line and only holds back a 😴 the user is already owed. Anything unrecognized still
gets its second go — it reads yt-dlp's prose, so the default has to be the old behaviour — and one
unrecognized line among several is enough, since `--ignore-errors` prints one per carousel item.
That exception also carries the reason in its message: it is what ends up under `Failed to process`,
which otherwise says nothing but `stdout: ''`.

### Video preparation (`src/Tools/Ffprobe/TelegramVideoPreparer.cs`)

Exists because the Bot API never inspects an upload: omit width/height/duration and they stay zero,
and mobile clients lay the player out from the message, squashing the frame. It reports **display**
dimensions (SAR and rotation applied) and throws for: still images (`_pipe`/`image*` format names —
kkinstagram sometimes serves one under `video/*`), a missing or attached-pic-only video stream,
non-H.264 codecs, and files over 50 MB. A throw is how the next downloader gets its turn. There is
deliberately **no re-encoding** — rejection is a guard, not a normal path.

ffprobe's JSON is deserialized into the records in `src/Tools/Ffprobe/FfprobeOutput.cs`, which model its
wire format including the oddities: the duration is a string and can be `N/A`, the pixel aspect ratio
is `"n:d"` and can be `N/A` or `0:1`, the rotation hides in `side_data_list` and comes as 90 or -90,
cover art is a video stream with `attached_pic`. Decoding all of that — including the display size —
lives on those records, so the preparer only applies Telegram's rules. Every part is optional, so
absence is modelled rather than guarded against, and unknown fields are ignored (ffprobe prints far
more than this reads). `FfprobeOutputTests` pins what each absence and each oddity reads as.

`ProcessRunner` is the single entry point for yt-dlp/ffmpeg/ffprobe: argument list (no shell
quoting), timeout that kills the whole process tree, both streams read concurrently.

## Configuration

Everything lives under the `UpDownLoaderBot` section of `src/appsettings.json`; environment
variables override with `__` for nesting (`UpDownLoaderBot__Telegram__Token`), which is how Docker
supplies the token. Each platform owns a section — `Instagram:Downloaders:{KkInstagram,YtDlp}`,
`TikTok:Downloaders:{TnkTok,YtDlp}`, `Instagram:YtDlp:CookiesFile` — and its own `Add<Name>` throws
at startup if every one of its downloaders is off. Only the token ever comes from the environment,
so these names live in `src/appsettings.json` alone.

The token in `src/appsettings.json` belongs to a **test bot** and is there on purpose — leave it
alone, and do not suggest emptying it, moving it out or rewriting history. The production token
never enters the repository: it comes from the `TELEGRAM_TOKEN` secret, which the deploy writes into
`.env` on the server as `UpDownLoaderBot__Telegram__Token`.

## Cookies

`cookies/InstagramCookies.txt` (Netscape format, gitignored) is the deployed file; yt-dlp works on a
copy at `cookies/InstagramCookies.session.txt` and rewrites it every run, so the refreshed session
survives restarts and redeploys. **Deleting the session file is the signal to re-seed from the
deployed cookies** — the deploy does exactly that when the `INSTAGRAM_COOKIES` secret changed.
**A missing cookies setting is said out loud at startup**, not only a missing file: Instagram serves
video to signed-in users alone, so an absent or misspelled
`UpDownLoaderBot:Instagram:YtDlp:CookiesFile` otherwise reads exactly like a key that was never
there, and the first sign would be a 😴 on every Instagram link in production.

`PrepareCookiesFile` / `ResolveCookiesFile` in `YtDlpDownloaderBase` implement this; the latter also
searches upward from the app base directory so a local `dotnet run` finds the repo-root `cookies/`.
Handed a `*.session.*` file (`IG_COOKIES` in a test, say), the former uses it as it is rather than
deriving a second level from it.

## Tests (`tests/`, xUnit)

The folders mirror `src/`, so where a test lives says what it covers: `tests/Core/`,
`tests/Tools/Ffprobe/`, `tests/Providers/Instagram/` and so on, with the namespaces following. Two
files sit outside that mirror on purpose: `ArchitectureTests` at the root, because it is about the
tree rather than a class, and `tests/Support/`, holding what more than one test needs: `Ffmpeg.cs`
for everything that wants a real video (`Run`, `Unavailable`, `EncoderAvailable` — `Unavailable`
**returns** the reason instead of logging it, so each caller writes it to its own
`ITestOutputHelper`) and `StubHttp.cs` for the two mirror downloaders, which are the same test
written against two hosts.

- `KkInstagramDownloaderTests`, `TikTokMirrorDownloaderTests` — stubbed `HttpMessageHandler`, fully
  offline. Both pin the rewritten URL, the bot UA, the naming by post id and both 60 MB limits.
- `TikTokLinksTests` — every link shape against the real pattern, including that a short link's code
  becomes the id and that the `?_t=` tail stays out of the URL.
- `TelegramVideoPreparerTests` — builds real files with ffmpeg (the metadata edge cases cannot be
  faked). Each test **returns early and passes** when ffmpeg is missing, so a bare runner stays
  green — install ffmpeg or the test proves nothing.
- `YtDlpDownloaderTests` — one `[Fact(Skip = …)]` integration test hitting live Instagram; run it by
  hand with cookies in `IG_COOKIES`. The rest are offline.
- `MediaLinkParserTests` — the intake against the real patterns, including that **registration order,
  not position in the text, decides** between two platforms (pinned with a stand-in platform rather
  than TikTok, so the test stays a test of `Core/` against the interface alone). That is a deliberate
  simplification, not the intended answer — see `backlog.md` §8.
- `MediaFetcherTests` — the fallback, which used to be unreachable inside the worker. Needs **no seam
  in production code**: "the preparer refused this file" is had by writing three bytes of text into a
  `.mp4`, refused whether ffprobe rejects it or is missing. Only one test there wants a real video and
  builds it with ffmpeg.
- `ArchitectureTests` — the layer boundaries above. Its first test asserts that it found sources at
  all, because a guard that reads nothing passes every other rule for the wrong reason.

CI (`.github/workflows/deploy.yml`) runs `dotnet test --configuration Release`, then builds and
pushes to GHCR and deploys over SSH on every push to `master`.

## Conventions

**Classes declare a plain constructor, not a primary one.** A primary constructor's parameter stays
in scope across every member, so it sits next to the field it initialized as a second way to reach
the same thing — and for an `IEnumerable` dependency that is a trap, since touching the parameter
instead of the field enumerates it again. Records keep their positional parameters: there the
parameters *are* the data.

**A result is named before it is used.** Two rules, and the second one has a boundary worth keeping.

`await` never appears inside a `return` expression, a constructor call or an argument list — the
result goes into a local first. `new DownloadedPost([await RunYtDlp(…)])` hid both the call and the
wrapping in one line; two lines say what happened and then what was built. Same reason `ProcessRunner`
awaits its two stream tasks into locals: the order it reads them in is now visible (they are started
before `WaitForExitAsync`, so they are still read concurrently).

**Work does not fuse with the construction on top of it.** Where a `return` both did something
substantial — ran a process, parsed, matched a regex — and built or validated the answer, the two are
split: `Match(text)` before `new MediaLink(…)`, `Deserialize(…)` before `?? throw`, `Replace(…)`
before `new Uri(…)`, `BuildArguments(…)` before `ProcessRunner.Run(…)`.

What this deliberately leaves alone, so it does not get "fixed" later:

- **one operation expressed as a chain** — `_platforms.Select(…).FirstOrDefault(…)`,
  `languageCode?.Split('-')[0].ToLowerInvariant()`. Nothing is fused; there is only one step.
- **`?? throw` / `? … : throw` on a value already in a local** — that is the `Require*`/`Ensure*`
  style, and the validation is the point of the line.
- **`is { } x` binding on a plain expression** — its whole job is to name the result, so it is the
  rule being followed, not broken. An `await` inside one is still an `await` inside an expression.
- **a trivial lookup as an argument** — `BotTexts.StartInstructions.For(languageCode)` passed to
  `SendMessage`. The rule is about hidden work, not about argument lists.
- **decoding on the `FfprobeOutput` records** — expression-bodied members are where that wire format
  is meant to be read; see the section above.

**A class that registers dependencies is named `<Name>ServiceCollectionExtensions`.** That is the
framework's own suffix, so one search for `ServiceCollectionExtensions` finds every place the
application is wired, and a newcomer already knows what to search for. The method inside stays
`Add<Name>`. Nothing registers services under any other name — `Program.cs` calls these and composes
nothing itself.

**A `partial` class means `[GeneratedRegex]` and nothing else.** The source generator writes the
method body into a second part of the class, so the keyword is a requirement, not a hint that
something is meant to be extended — `InstagramLinks` and `KkInstagramDownloader` are the only two.
The attribute is worth its keyword because it makes the compiler read the pattern: an unclosed group
fails the build instead of throwing on the first message that arrives.

Comments here explain *why*, usually the non-obvious external constraint (a Bot API limit, a yt-dlp
quirk, an ffprobe oddity) — match that register rather than describing what the code does. Code,
comments and docs are in English. **A comment has to earn its place**, so keep it short and only
where a reader would otherwise ask "why":

- a `///` doc comment on a type or member whose name does not already say it all — not on every
  property, and not one that restates the name (`Enables the yt-dlp downloader` on `YtDlp`);
- an inline comment on a line that looks wrong but isn't, or that a reader would "fix";
- one fact in one place: what a class doc says is not repeated on its members, and the long story
  belongs in this file or `readme.md`, with the comment keeping the one sentence the code needs;
- no history (`which 👎 did`), no plans (`a future YouTube downloader`), no narrating the next line.

User-facing bot text lives in `src/Bot/BotTexts.cs`: one `LocalizedText` (`src/Bot/LocalizedText.cs`)
per message, holding all five languages in alphabetical order. `LocalizedText.For` is the only way in —
it picks by the `language_code` Telegram sends (`be`, `pl`, `ru`, `uk`, English for everything else
and for a channel post, which has no sender to ask), and the languages are private so nobody can
take one and skip that fallback. A new message is another `LocalizedText`, not another switch; a new
language is a constructor parameter, a property and a switch arm, and the compiler then names every
message missing it. Keep the translations saying the same things — `BotTextsTests` checks the link
shapes and the 😴 are in all of them.

`backlog.md` is untracked working notes (in Russian): known rough edges, decisions deliberately
left until a second example exists, and a log of what has been done. Some entries are stale
relative to the code — verify before acting on one.
