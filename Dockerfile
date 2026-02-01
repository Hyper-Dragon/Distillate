# Stage 1: Build the C# app
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /src

# Copy solution and project files
COPY Distillate.sln .
COPY src/Distillate.Core/Distillate.Core.csproj src/Distillate.Core/
COPY src/Distillate.Web/Distillate.Web.csproj src/Distillate.Web/

# Restore dependencies
RUN dotnet restore -a $TARGETARCH

# Copy source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/Distillate.Web/Distillate.Web.csproj -c Release -a $TARGETARCH -o /app --no-restore

# Stage 2: Final Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS final
WORKDIR /app

# Install Tini, Chromium, and required system libs
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
    libpango-1.0-0 \
    libcairo2 \
    libasound2 \
    fonts-liberation \
    fonts-noto-color-emoji \
    && rm -rf /var/lib/apt/lists/*

# Set Playwright env vars to skip browser download
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
ENV CHROMIUM_PATH=/usr/bin/chromium

# Default configuration
ENV CONCURRENCY=1
ENV MAX_TIME_MS=180000
ENV MAX_IMAGE_BYTES=209715200
ENV GC_MODE=normal
ENV STABILIZATION_MS=10000
ENV LOG_LEVEL=Information
ENV PORT=8080

COPY --from=build /app .

# Expose port
EXPOSE 8080

# Use tini as the entrypoint to handle signals and zombies
ENTRYPOINT ["/usr/bin/tini", "--"]
CMD ["dotnet", "Distillate.Web.dll"]
