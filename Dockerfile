# Puffinote for a NAS: builds the web client, then runs the dependency-free Node server.
FROM node:22-alpine AS build
WORKDIR /src
RUN corepack enable
COPY pnpm-lock.yaml pnpm-workspace.yaml package.json .npmrc ./
COPY packages/text/package.json packages/text/
COPY packages/markdown/package.json packages/markdown/
COPY packages/ui/package.json packages/ui/
COPY apps/web/package.json apps/web/
RUN pnpm install --frozen-lockfile --filter @puffinote/web...
COPY tsconfig.base.json ./
COPY packages packages
COPY apps/web apps/web
RUN pnpm --filter @puffinote/web build

FROM node:22-alpine
# The server listens on 0.0.0.0 here, so it refuses to start without PUFFINOTE_TOKEN (pass it with -e), unless
# PUFFINOTE_ALLOW_NO_TOKEN=1 says a reverse proxy in front authenticates. /api/info stays public for the check.
ENV NODE_ENV=production PORT=8790 PUFFINOTE_STATIC=/app/web PUFFINOTE_ROOTS="Notes=/data"
WORKDIR /app
COPY apps/server/package.json server/
COPY apps/server/src server/src
# The shared file code runs from source too. Linked from outside node_modules, as pnpm does, since Node strips
# types only from files outside node_modules.
COPY packages/node-files/package.json packages/node-files/
COPY packages/node-files/src packages/node-files/src
RUN mkdir -p server/node_modules/@puffinote \
  && ln -s ../../../packages/node-files server/node_modules/@puffinote/node-files
COPY --from=build /src/apps/web/dist web
USER node
EXPOSE 8790
VOLUME ["/data"]
HEALTHCHECK --interval=30s --timeout=3s CMD wget -qO- http://127.0.0.1:8790/api/info >/dev/null || exit 1
CMD ["node", "server/src/main.ts"]
