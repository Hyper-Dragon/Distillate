# =========================
# Runtime base image
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Install Chromium dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    tini \
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
    libpango-1.0-0 \
    libcairo2 \
    libasound2t64 \
    libxshmfence1 \
    fonts-liberation \
    fonts-noto-color-emoji \
    # Additional X11/GTK libs required by chrome-headless-shell
    libxfixes3 \
    libxrender1 \
    libxss1 \
    libgtk-3-0 \
    libxcb1 \
    libxcursor1 \
    libx11-6 \
    && rm -rf /var/lib/apt/lists/*

ENV PORT=8080
ENTRYPOINT ["dotnet", "Distillate.Web.dll"]

# =========================
# Build + publish + Playwright install
# =========================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files and restore
COPY ["src/Distillate.Web/Distillate.Web.csproj", "src/Distillate.Web/"]
COPY ["src/Distillate.Core/Distillate.Core.csproj", "src/Distillate.Core/"]
RUN dotnet restore "src/Distillate.Web/Distillate.Web.csproj"

# Copy full source
COPY . .

WORKDIR /src/src/Distillate.Web

# Build project into /app/build so Playwright scripts (playwright.ps1) are available
RUN dotnet build Distillate.Web.csproj -c $BUILD_CONFIGURATION -o /app/build

# Install Playwright browsers using the playwright.ps1 from build output
WORKDIR /app/build
RUN pwsh -c "& ./playwright.ps1 install chromium --with-deps"

# Publish project for linux-x64 into /app/publish
WORKDIR /src/src/Distillate.Web
RUN dotnet publish Distillate.Web.csproj \
    -c $BUILD_CONFIGURATION \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish

# Copy Playwright browsers from build cache into publish and ensure permissions
RUN mkdir -p /app/publish/.ms-playwright \
    && cp -a /root/.cache/ms-playwright/* /app/publish/.ms-playwright/ 2>/dev/null || true \
    && chmod -R a+rx /app/publish/.ms-playwright

# =========================
# Final runtime image
# =========================
FROM base AS final
WORKDIR /app
# Copy published app from build stage
COPY --from=build /app/publish .
# Make Playwright browsers executable if present
RUN if [ -d "/app/.ms-playwright" ]; then chmod -R a+rx /app/.ms-playwright; fi

# Point Playwright to the browsers we bundled during build and avoid runtime downloads
ENV PLAYWRIGHT_BROWSERS_PATH=/app/.ms-playwright
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1

ENTRYPOINT ["dotnet", "Distillate.Web.dll"]
