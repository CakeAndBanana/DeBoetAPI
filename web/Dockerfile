# Build context is the repository root (see compose.yaml), so paths are repo-relative.
FROM node:24-alpine AS deps
WORKDIR /app
# Lockfile only, so this layer caches across source-only changes.
COPY web/package.json web/package-lock.json ./
RUN npm ci

FROM node:24-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY web/ ./
# Next bakes nothing secret at build time here — every value is read at runtime — so no
# build args are needed.
RUN npm run build

FROM node:24-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production \
    PORT=3000 \
    HOSTNAME=0.0.0.0

# `output: standalone` emits a self-contained server plus only the node_modules it needs.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public

# The node image ships a non-root `node` user.
USER node
EXPOSE 3000
CMD ["node", "server.js"]
