# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY src/UpDownLoaderBot.csproj ./
RUN dotnet restore

COPY src/ ./
RUN dotnet publish -c Release -o /app --no-restore

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# yt-dlp (arch-independent zipapp, needs python3) + ffmpeg for muxing.
# icu-libs: ICU for .NET globalization (Alpine images ship without it).
# --no-cache leaves no package index behind, so no manual cleanup needed.
RUN apk add --no-cache ffmpeg python3 ca-certificates icu-libs \
    && wget -q https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -O /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp

COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "UpDownLoaderBot.dll"]
