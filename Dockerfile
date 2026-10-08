# Minimal ffmpeg/ffprobe built from the pinned upstream release tarball. Javbuddy needs to decode
# anything (all decoders, demuxers and filters stay), encode WebP stills and VP9 clips, remux with
# stream copy, and run the null muxer for scene detection (which needs wrapped_avframe/pcm_s16le).
# Encoders, muxers and protocols are cut to that, so the result is a fraction of the size of a
# full-featured static build. libwebp/libvpx/libdav1d are linked dynamically against the distro's
# libraries, which the final stage installs (so their security fixes arrive via apt). Bump by
# updating FFMPEG_VERSION and FFMPEG_SHA256 (the sha256 of ffmpeg-<version>.tar.xz).
FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS ffmpeg
ARG FFMPEG_VERSION=9.0.1
ARG FFMPEG_SHA256=cf38e0e28c7e5605942c4a77755349b0145804a397af37eb1fb4c77cb237f635
WORKDIR /src
# Unpinned apt packages: the base image tag floats, and exact distro versions disappear from the archive.
# hadolint ignore=DL3008
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        ca-certificates curl xz-utils build-essential nasm pkg-config \
        libwebp-dev libvpx-dev libdav1d-dev \
    && rm -rf /var/lib/apt/lists/* \
    && curl -fsSL -o ffmpeg.tar.xz "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && echo "${FFMPEG_SHA256}  ffmpeg.tar.xz" > ffmpeg.tar.xz.sha256 \
    && sha256sum -c ffmpeg.tar.xz.sha256 \
    && tar -xJf ffmpeg.tar.xz --strip-components=1
RUN ./configure --prefix=/out --disable-doc --disable-debug --disable-ffplay --disable-network \
        --disable-autodetect --disable-hwaccels --disable-devices \
        --disable-encoders --enable-encoder=libwebp,libvpx_vp9,wrapped_avframe,pcm_s16le \
        --disable-muxers --enable-muxer=mp4,mov,matroska,webm,avi,mpegts,asf,null,image2,webp \
        --disable-protocols --enable-protocol=file,pipe \
        --enable-libwebp --enable-libvpx --enable-libdav1d \
        --disable-shared --enable-static \
    && make -j"$(nproc)" \
    && make install \
    && strip /out/bin/ffmpeg /out/bin/ffprobe


FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS build
# Set by BuildKit to the platform being built (amd64 or arm64). Each architecture is built natively
# (CI uses a runner per platform), so the SDK here already matches it; only the RID needs mapping.
ARG TARGETARCH
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY Javbuddy/Javbuddy.csproj Javbuddy/
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; esac \
    && echo "$rid" > /rid \
    && dotnet restore Javbuddy/Javbuddy.csproj -r "$rid"

COPY . .
RUN dotnet publish Javbuddy/Javbuddy.csproj -c Release -r "$(cat /rid)" --self-contained false -o /app/publish


# Collects everything the chiseled runtime image can't install itself (it has no shell or apt):
# the native libraries Javbuddy needs plus their shared-library dependencies, as a ready-to-copy
# rootfs. Built on the full aspnet image, which shares the chiseled image's glibc.
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute AS native-libs
# libfontconfig1: SkiaSharp. libwebp7, libvpx12, libdav1d7: shared libraries the ffmpeg build links
# against. libmediainfo0v5: native MediaInfo (pulls in libzen and its own dependencies).
# busybox-static: a ~3 MB shell and basic tools, so `docker exec <container> sh` works.
# Unpinned apt packages: the base image tag floats, and exact distro versions disappear from the archive.
# hadolint ignore=DL3008
RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        libfontconfig1 libmediainfo0v5 libwebp7 libvpx12 libdav1d7 busybox-static \
    && rm -rf /var/lib/apt/lists/*

COPY --from=ffmpeg /out/bin/ffmpeg /out/bin/ffprobe /rootfs/usr/local/bin/
COPY --from=build /app/publish/libSkiaSharp.so /tmp/libSkiaSharp.so

# Copy each binary's resolved shared-library closure into /rootfs at the same paths, minus the
# libraries the chiseled base already ships (glibc, libgcc, libstdc++, zlib). cp -L stores the real
# file under the soname the loader asks for. The package ships only libmediainfo.so.0, but the .NET
# wrapper DllImports "mediainfo" and so probes libmediainfo.so, which the -dev package would
# normally provide; add that name too. /etc/fonts is fontconfig's configuration. ldd fails the
# build if a dependency is missing, so a broken closure can't ship.
# hadolint ignore=DL4006
RUN set -eu; \
    mi="$(find /usr/lib -name libmediainfo.so.0 | head -n1)"; \
    for bin in /rootfs/usr/local/bin/ffmpeg /rootfs/usr/local/bin/ffprobe /tmp/libSkiaSharp.so "$mi"; do \
        ldd "$bin" | grep -q "not found" && { ldd "$bin"; exit 1; }; \
        ldd "$bin" | awk '$3 ~ /^\// {print $3}' | grep -Ev '/(libc|libm|libdl|libpthread|librt|libgcc_s|libstdc\+\+|libz)\.so' \
            | while read -r lib; do cp -L --parents "$lib" /rootfs; done; \
    done; \
    cp -L "$mi" "/rootfs${mi%.0}"; \
    cp -a --parents /etc/fonts /usr/share/fontconfig /rootfs; \
    mkdir -p /rootfs/usr/bin && cp /bin/busybox /rootfs/usr/bin/busybox && for a in $(/rootfs/usr/bin/busybox --list); do [ "$a" = busybox ] || ln -sf busybox "/rootfs/usr/bin/$a"; done; \
    mkdir -p /dirs/data /dirs/cache /dirs/objects

# chiseled-extra: distroless (no shell, apt or root-owned writable dirs) with ICU and tzdata, so
# culture handling and TZ keep working.
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-extra AS final

COPY --from=native-libs /rootfs/ /

# Storage tiers, owned by the unprivileged app user (a chiseled image has no apt, and busybox's sh is not worth a RUN layer just to mkdir/chown them,
# so copy empty directories in with --chown instead). WORKDIR creates /app root-owned, so /app
# itself is copied the same way.
COPY --from=native-libs --chown=$APP_UID:$APP_UID /dirs/data /data
COPY --from=native-libs --chown=$APP_UID:$APP_UID /dirs/cache /cache
COPY --from=native-libs --chown=$APP_UID:$APP_UID /dirs/objects /objects
COPY --from=native-libs --chown=$APP_UID:$APP_UID /dirs/objects /app
WORKDIR /app
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
