FROM node:22-alpine
WORKDIR /app
COPY package.json ./
COPY src ./src
COPY vendor ./vendor
RUN mkdir -p /data && chown -R node:node /data
USER node
ENV BIND=0.0.0.0 PORT=5090 STATE_FILE=/data/gateway-state.json
EXPOSE 5090
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO- http://127.0.0.1:5090/health || exit 1
CMD ["node", "src/index.mjs"]
