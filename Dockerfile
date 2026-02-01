# =========================
# Stage 1: Build + Playwright install
# =========================
# Built on the native architecture of the builder and cross-compiled to
# TARGETARCH. .NET publishes RID-specific output without executing it, so an
# amd64 host produces a correct arm64 app without QEMU.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG DISTILLATE_BUILD=dev
WORKDIR /src

COPY Distillate.sln .
COPY src/Distillate.Core/Distillate.Core.csproj src/Distillate.Core/
COPY src/Distillate.Web/Distillate.Web.csproj src/Distillate.Web/
COPY tests/Distillate.Tests/Distillate.Tests.csproj tests/Distillate.Tests/

RUN dotnet restore -a $TARGETARCH
COPY src/ src/

RUN dotnet build src/Distillate.Web/Distillate.Web.csproj \
    -c Release -a $TARGETARCH --no-restore

RUN dotnet tool install --global Microsoft.Playwright.CLI
ENV PATH="${PATH}:/root/.dotnet/tools"

RUN playwright install chromium

RUN dotnet publish src/Distillate.Web/Distillate.Web.csproj \
    -c Release -a $TARGETARCH -o /app --no-restore \
    -p:DistillateBuild=$DISTILLATE_BUILD


# =========================
# Stage 2: Runtime
# =========================
# `-noble` explicitly: this configuration was proven on Ubuntu 24.04, and the
# library names below (`libasound2t64`) are the Noble ones. Leaving it as the bare
# default tag would quietly change the distro under a working deployment.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Chromium and the shared libraries it needs. Installed from the distro rather
# than left to Playwright's download because the two are not equivalent: the apt
# package pulls in the transitive libraries this list does not name.
RUN apt-get update && apt-get install -y --no-install-recommends \
    tini \
    chromium \
    libnss3 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxrandr2 \
    libgbm1 \
    libxfixes3 \
    libpango-1.0-0 \
    libcairo2 \
    libasound2t64 \
    fonts-liberation \
    fonts-noto-color-emoji \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy app AND Playwright browser cache
COPY --from=build /app /app
COPY --from=build /root/.cache/ms-playwright /root/.cache/ms-playwright

ENV PLAYWRIGHT_BROWSERS_PATH=/root/.cache/ms-playwright

# CHROMIUM_PATH is deliberately NOT set. The code default is /usr/bin/chromium and
# DistillateService uses Playwright's bundled browser when that path is not a real
# file, so leaving it unset is what makes the image work whether or not the distro
# package installed a binary you can actually execute.

# DistillateService launches Chromium with Headless = true, so there is no Xvfb
# and no DISPLAY here: a virtual X server would be a process nobody waits for,
# started by a shell that would then sit between PID 1 and the signal handler.

ENV CONCURRENCY=1
ENV MAX_TIME_MS=180000
ENV MAX_IMAGE_BYTES=209715200
ENV GC_MODE=normal
ENV STABILIZATION_MS=10000
ENV LOG_LEVEL=Information
ENV PORT=8080
ENV MODE=rest

EXPOSE 8080

# Runs as root. DistillateService passes --no-sandbox to Chromium in its own
# launch arguments, so a non-root user here would not re-enable the sandbox — it
# would only add a UID mismatch on the Playwright browser cache and a failure mode
# with nothing to show for it. The container is the boundary; the service writes
# nothing to disk and needs no capabilities beyond the network.
#
# tini as PID 1 because an extraction may run for MAX_TIME_MS and the signal has
# to reach dotnet directly. The probe needs nothing from the runtime: no shell,
# no curl, no wget — it is the application checking itself.
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 \
    CMD ["dotnet", "Distillate.Web.dll", "--healthcheck"]

ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["dotnet", "Distillate.Web.dll"]
