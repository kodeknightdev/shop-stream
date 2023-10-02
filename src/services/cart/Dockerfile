FROM node:18.17.1-alpine3.18
ENV NODE_ENV=production
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY src/ src/
USER node
EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=5 CMD wget -qO- http://localhost:3000/health >/dev/null || exit 1
CMD ["node", "src/server.js"]
