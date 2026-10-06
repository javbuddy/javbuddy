# Static ffmpeg/ffprobe from BtbN's FFmpeg-Builds, for a current full-featured ffmpeg rather than
# the distro's. Pinned to a month-end autobuild because BtbN deletes daily ones after about two
# weeks (month-end ones are kept for about two years). Bump by picking a newer month-end
# autobuild-* release and updating all three values.
FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS ffmpeg
ARG FFMPEG_RELEASE=autobuild-2026-08-31-13-27
ARG FFMPEG_ASSET=ffmpeg-n9.0.1-11-ge47273f4d9-linux64-gpl-9.0.tar.xz
ARG FFMPEG_SHA256=182c1b509720e939bb47bfb47dc29cc0c298640401128e3dce8627d10707eb5a
WORKDIR /ffmpeg
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        ca-certificates curl xz-utils \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL -o ffmpeg.tar.xz "https://github.com/BtbN/FFmpeg-Builds/releases/download/${FFMPEG_RELEASE}/${FFMPEG_ASSET}" \
    && echo "${FFMPEG_SHA256}  ffmpeg.tar.xz" > ffmpeg.tar.xz.sha256 \
    && sha256sum -c ffmpeg.tar.xz.sha256 \
    && tar -xJf ffmpeg.tar.xz --strip-components=2 --wildcards '*/bin/ffmpeg' '*/bin/ffprobe' \
    && rm ffmpeg.tar.xz ffmpeg.tar.xz.sha256 \
    && ./ffmpeg -hide_banner -version


FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY Javbuddy/Javbuddy.csproj Javbuddy/
RUN dotnet restore Javbuddy/Javbuddy.csproj -r linux-x64

COPY . .
RUN dotnet publish Javbuddy/Javbuddy.csproj -c Release -r linux-x64 --self-contained false -o /app/publish


FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute AS final
WORKDIR /app

# libfontconfig1: SkiaSharp. tzdata: honors TZ. libcurl3t64-gnutls, libmms0: libmediainfo.so.
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        libfontconfig1 tzdata libcurl3t64-gnutls libmms0 \
    && rm -rf /var/lib/apt/lists/*

# ffmpeg/ffprobe come from the pinned static build above rather than apt: trickplay decodes a large
# library for days, so a current build matters. Security fixes arrive by bumping the pin.
COPY --from=ffmpeg /ffmpeg/ffmpeg /ffmpeg/ffprobe /usr/local/bin/

# MediaInfo.Core.Native ships its native libraries only under distro-specific NuGet RIDs that the
# SDK's RID graph no longer recognizes, so `-r linux-x64` skips them. Copy the two .so files out of
# the NuGet cache instead. The package has no ubuntu.26.04-x64 build; ubuntu.25.10-x64 resolves all
# dependencies on this base image. LD_LIBRARY_PATH (not ldconfig) is needed because .NET's
# DllImport resolver does not consult ld.so.cache.
ENV LD_LIBRARY_PATH=/opt/mediainfo-libs
COPY --from=build /root/.nuget/packages/mediainfo.core.native/*/runtimes/ubuntu.25.10-x64/native/libmediainfo.so /root/.nuget/packages/mediainfo.core.native/*/runtimes/ubuntu.25.10-x64/native/libzen.so.0 $LD_LIBRARY_PATH/

# Storage tiers, owned by the unprivileged app user
RUN mkdir -p /data /cache /objects && chown -R $APP_UID:$APP_UID /data /cache /objects /app

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# SkiaSharp decodes allocate 20-100 MB native bitmaps; with glibc's default dynamic mmap threshold
# they fragment the heap and are never returned to the OS (idle RSS ~1.5 GB). Setting the trim
# threshold pins the mmap threshold at 128 KB so each bitmap is unmapped when freed (idle RSS
# ~280 MB). MALLOC_ARENA_MAX=2 made it worse.
ENV MALLOC_TRIM_THRESHOLD_=131072

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Three storage tiers, each its own volume so they can sit on different storage classes:
# - /data: SQLite DB + DataProtection keys. Needs strong POSIX locking; never NFS.
# - /cache: disposable, regenerable image cache. Fast disk; safe to lose.
# - /objects: durable object store (actor photos, trickplay). Bulk capacity; NFS is fine.
ENV ConnectionStrings__Default="Data Source=/data/Javbuddy.db" \
    DataProtection__KeysPath=/data/dataprotection-keys \
    ImageCache__Path=/cache \
    ObjectStore__Path=/objects
VOLUME ["/data", "/cache", "/objects"]

USER $APP_UID

ENTRYPOINT ["dotnet", "Javbuddy.dll"]
