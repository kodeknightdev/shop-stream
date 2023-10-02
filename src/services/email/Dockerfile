FROM node:18.17.1-alpine3.18 AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY tsconfig.json tsconfig.build.json ./
COPY src/ src/
RUN npm run build && npm prune --omit=dev

FROM node:18.17.1-alpine3.18
ENV NODE_ENV=production
WORKDIR /app
COPY --from=build /app/node_modules node_modules/
COPY --from=build /app/dist dist/
COPY package.json ./
COPY templates/ templates/
USER node
EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=5 CMD wget -qO- http://localhost:3000/health >/dev/null || exit 1
CMD ["node", "dist/main.js"]
