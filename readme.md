# UpDownLoaderBot

[@UpDownLoaderBot](https://t.me/UpDownLoaderBot) — a Telegram bot that downloads
Instagram Reels and TikTok videos and sends them straight back to the chat. Just give it a link,
in a direct message, a group or a channel. When a download fails, the bot reacts to
the link with 😴. A link with nothing to download — a post of photos only, a TikTok
slideshow — gets no reaction at all: there was no video to send, so nothing went wrong.

`/start` — the button Telegram shows on the first visit — answers with a short
instruction: what to send, where the bot works, and what the 😴 means. It follows the
language of the Telegram client that asked: Russian, Ukrainian, Polish and Belarusian
are translated, everyone else gets English.

To use it in a group, make the bot an administrator — no permissions need to be
granted. Without that, Telegram does not deliver ordinary messages to it, so it
never sees the link.

## Built with

- .NET 10 and ASP.NET Core for the host; the bot itself is a `BackgroundService`
  (`TelegramBotWorker`) running on long polling
- [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot) 22.10
- `yt-dlp` plus `ffmpeg`/`ffprobe` to download and inspect video
- Docker (Alpine) with GitHub Actions and GHCR for build and deployment
- xUnit for tests

Alongside the bot the app serves `GET /health`, which calls Telegram's `getMe` and
answers `200` with the bot's username, or `503` with the error. That is port 8080 in
the container, and Kestrel's usual port locally.

## How it works

[ARCHITECTURE.md](ARCHITECTURE.md) is the map: a diagram of the components and who calls whom, what
each class does, one request step by step, and which method to open for which question.

The bot picks the first link it recognizes out of the message — an Instagram `reel`,
`reels`, `p` or `tv` link, or any TikTok link — `/@user/video/`, `/@user/photo/`,
`/share/video/`, a short `vm.`/`vt.tiktok.com` or `/t/` one, a TikTok Lite or
`tiktokv.com` share link, or an old `/embed/` or `/v/….html` one — and hands it to the downloaders that take that link, in turn,
stopping at the first success. One link means one video: a reel, an IGTV post and a
TikTok video have only one anyway, and of an Instagram carousel the first video is what
comes back. It arrives as a reply to the link, with the link as its caption, and named
after the post.

A downloader decides for itself whether a link is its business, and claims only links
whose content it can deliver whole — otherwise it would answer with a part of a post and
the one that could have returned all of it would never run.

A link does not say whether the post holds a video: an Instagram `p` link may be a photo,
a carousel or a video. Only a download finds out. When yt-dlp reaches the post and every
item reports `No video formats found!`, there is *nothing to send*; the same holds for a
link no downloader takes. Both end in silence. Anything else that leaves the bot without
a video is a failure, and gets the 😴.

Each platform has the same pair: a mirror that answers a rewritten link with the video
file itself, and yt-dlp behind it.

**Instagram**

1. **kkinstagram** — a plain HTTP request to an Instagram mirror. Needs no
   authentication and returns a ready-made progressive file (H.264 + AAC). Being one
   request it returns one file, so it takes only the links that hold exactly one video:
   `reel`, `reels` and `tv`. A `p` post may be a carousel, and goes straight to yt-dlp;
2. **yt-dlp** — the dependable fallback, and the one that needs
   [Instagram cookies](#instagram-cookies). It asks for the progressive rendition
   too: Instagram's DASH ladder is VP9-only and reaches 1440×2560 at ~70 MB, past
   the Bot API's limit and unplayable on iOS. A carousel post is a playlist to yt-dlp,
   and `--no-playlist` does not collapse it — the extractor ignores the flag and walks
   every entry — so `--max-downloads 1` stops it at the first video it manages to
   download, `--ignore-errors` lets it step over a photo on the way there
   (`No video formats found!`) and `-I 1:10` bounds how far into the post it looks.
   Both of those make yt-dlp exit non-zero on a perfectly good download, so the file
   it printed, not the exit code, is what decides success.

**TikTok** — no cookies at either step, so nothing has to be kept up to date for it.

1. **tnktok** — the same trick against `d.tnktok.com`, which answers a bot User-Agent
   with the H.264 file. Only the host is swapped, short links included — the mirror
   follows `vm.tiktok.com/…` itself — so this is the fast path for every shape but a
   `/photo/` slideshow, which holds no video at all;
2. **yt-dlp** — the fallback, which takes every TikTok link but a `/photo/` slideshow:
   it has no extractor for one, and with neither downloader taking it the link gets no
   reaction rather than a run that can only fail. TikTok offers its 720p
   rendition in H.265 only, which the Bot API's clients cannot be relied on to play and
   the bot refuses, so the format sort asks for H.264 before it asks for height.

Any of the four downloaders can be switched off with a flag — see
[Configuration](#configuration).

Before the upload, the file goes through `ffprobe`. The reason is that the Bot API
never inspects what a bot uploads: leave out `width`, `height` and `duration` and
they stay zero in the message. Desktop clients read the local file and render it
correctly, but Android and iOS lay the player out from the message, and the frame
comes out squashed. So the bot measures the video itself:

- dimensions are the ones to display, with non-square pixels (SAR) and rotation
  applied;
- a file that cannot be sent is rejected: not a video (kkinstagram occasionally
  answers with a still image under a `video/*` content type), not H.264, or larger
  than 50 MB. A rejection is not the end of the request — the next downloader gets
  a turn.

There is deliberately no re-encoding: both downloaders already return H.264 within
the limit, so rejection guards against the unexpected rather than being a normal
path.

## Running locally

You need `yt-dlp` and `ffmpeg` (`ffprobe` ships in the same package):

```bash
brew install yt-dlp ffmpeg
```

Token and start-up:

```bash
export UpDownLoaderBot__Telegram__Token="<token from @BotFather>"
dotnet run --project src
```

Tests — no network or cookies needed; the video ones skip themselves when `ffmpeg`
is missing, so install it to actually run them:

```bash
dotnet test
```

## Instagram cookies

Instagram serves video only to signed-in users: an anonymous request gets a login
wall instead of the file, so `yt-dlp` needs the cookies of a logged-in session to
work at all. (The kkinstagram mirror does not, which is why it goes first — but it
cannot be relied on alone.)

Getting them takes a browser extension, since the cookies have to be in the Netscape
`cookies.txt` format that `yt-dlp` reads:

1. install [Cookie-Editor](https://cookie-editor.com) — it is available for Chrome,
   Firefox, Safari, Edge and Opera;
2. log in to Instagram in that browser and open the extension on any instagram.com page;
3. export the cookies choosing the **Netscape** format (not JSON);
4. paste the result into `cookies/InstagramCookies.txt`.

That file is what the app hands to `yt-dlp`. To check the export works:

```bash
yt-dlp --cookies cookies/InstagramCookies.txt "https://www.instagram.com/reel/<id>/"
```

For a deployment the same text goes into the `INSTAGRAM_COOKIES` secret instead, and
the deploy writes it to the server.

Cookies are as good as a password for the account — keep them out of git and out of
shared chats. Logging out of Instagram in that browser can invalidate the exported
session, so leave it signed in.

### How the session survives deploys

`yt-dlp` refreshes the Instagram session on every run, so a working copy lives next
to the deployed file:

- `cookies/InstagramCookies.txt` — whatever came from the `INSTAGRAM_COOKIES` secret;
- `cookies/InstagramCookies.session.txt` — the session, rewritten by `yt-dlp`.

On start-up the app uses the session if it exists and is not empty, otherwise it
seeds one by copying the deployed file. The deploy compares the secret against the
file on the server and overwrites `InstagramCookies.txt` only when it changed,
deleting the session as it does — a deleted session is the signal to start over from
the new cookies. An ordinary redeploy with an unchanged secret leaves the refreshed
session alone, and because `cookies/` is mounted read-write, it survives both
restarts and redeploys.

The logs say which happened: `Instagram cookies unchanged…` /
`New Instagram cookies deployed…` from the deploy, and `Reusing cookies session…` /
`Seeded the cookies session…` from the app at start-up.

A refreshed session never travels back into the GitHub secret on its own. If CI needs
a fresh one, copy `InstagramCookies.session.txt` off the server into the
`INSTAGRAM_COOKIES` secret by hand. The container writes that file as root, so
reading it on the host takes `sudo`.

## Configuration

Settings live under the `UpDownLoaderBot` section of `src/appsettings.json`. Any of
them can be overridden by an environment variable that spells the nesting with
double underscores — `UpDownLoaderBot__Telegram__Token`, or
`UpDownLoaderBot__TikTok__Downloaders__TnkTok=false` for one of the flags below. That is
how Docker supplies the token, and the token is the only one it supplies: every other
setting lives in `appsettings.json` alone, so the deploy and `docker-compose.yml` never
mention them.

Each platform owns a section, and at least one of its downloaders has to stay enabled —
the bot refuses to start otherwise.

| Setting                             | Purpose                                       |
|-------------------------------------|-----------------------------------------------|
| `Telegram:Token`                    | bot token                                     |
| `Instagram:Downloaders:KkInstagram` | whether the kkinstagram downloader is enabled |
| `Instagram:Downloaders:YtDlp`       | whether the yt-dlp downloader is enabled      |
| `Instagram:YtDlp:CookiesFile`       | path to the Instagram cookies for yt-dlp      |
| `TikTok:Downloaders:TnkTok`         | whether the tnktok downloader is enabled      |
| `TikTok:Downloaders:YtDlp`          | whether the yt-dlp downloader is enabled      |

TikTok has no cookies setting because it needs no account: both of its downloaders work
anonymously. Instagram serves video to signed-in users only — see
[Instagram cookies](#instagram-cookies).

Keep the token in an environment variable or a secret, never in the code. If it ever
lands in git history, revoke it in @BotFather and issue a new one.

## Known issue: `No module named expat`

On macOS 26 (Tahoe) the brew build of `yt-dlp` depends on `python@3.14`, whose
`pyexpat` module links against the system `/usr/lib/libexpat.1.dylib`, which lacks
the symbol it needs — so `yt-dlp` dies with `ERROR: No module named expat; use
SimpleXMLTreeBuilder instead`. Homebrew's expat does have the symbol, so pointing the
module at it fixes things:

```bash
SO=$(python3 -c "import pyexpat,os;print(pyexpat.__file__)")
install_name_tool -change /usr/lib/libexpat.1.dylib /opt/homebrew/opt/expat/lib/libexpat.1.dylib "$SO"
codesign --force --sign - "$SO"
python3 -c "import pyexpat; print('expat ok')" && yt-dlp --version
```

The next `brew reinstall/upgrade python@3.14` undoes it — if the error comes back,
run the commands again.

## Deployment

On every push to `master` (or manually through `workflow_dispatch`), the
`.github/workflows/deploy.yml` workflow builds the Docker image, pushes it to GHCR
(`:latest` and `:sha-<full commit sha>`), then connects over SSH and brings the new
version up with `docker compose pull && docker compose up -d`.

The server only needs Docker with the compose plugin and SSH access — the deploy
creates `DEPLOY_PATH`, `docker-compose.yml`, `.env` and the cookies itself. `.env`
receives the bot token and `APP_IMAGE` (the latest image tag), so a plain
`docker compose stop` / `docker compose up -d` works inside `DEPLOY_PATH`.

Repository secrets (Settings → Secrets and variables → Actions):

| Secret              | Purpose                                                                         |
|---------------------|---------------------------------------------------------------------------------|
| `SSH_HOST`          | server IP or hostname                                                           |
| `SSH_USER`          | SSH user                                                                        |
| `SSH_KEY`           | private SSH key (the public one goes in `authorized_keys`)                      |
| `SSH_PORT`          | SSH port                                                                        |
| `DEPLOY_PATH`       | path on the server, e.g. `/home/user/updownloaderbot`                           |
| `TELEGRAM_TOKEN`    | bot token, written into `.env` on the server                                    |
| `INSTAGRAM_COOKIES` | contents of `InstagramCookies.txt`, see [Instagram cookies](#instagram-cookies) |
