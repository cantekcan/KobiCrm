import { createRequire } from "node:module";
import { createGateway } from "./gateway.mjs";
import { StateStore } from "./state.mjs";

const require = createRequire(import.meta.url);
const { createFaturaClient } = require("../vendor/fatura/dist/index.js");

const env = (process.env.GIB_ENV ?? "TEST").toUpperCase();
if (!["TEST", "PROD"].includes(env)) { console.error("GIB_ENV TEST veya PROD olmalı."); process.exit(1); }
const token = process.env.GATEWAY_TOKEN ?? "";
if (token.length < 32 || /BURAYA|CHANGE/i.test(token)) { console.error("GATEWAY_TOKEN en az 32 karakterlik rastgele bir değer olmalı (yer tutucu kabul edilmez)."); process.exit(1); }
if (env === "PROD" && process.env.GIB_ALLOW_PROD !== "yes") {
  console.error("GIB_ENV=PROD GERÇEK mali belge üretir. Test portalında doğrulanmadan açılmamalıdır. Bilerek açmak için GIB_ALLOW_PROD=yes verin.");
  process.exit(1);
}

const bind = process.env.BIND ?? "127.0.0.1";
const port = Number(process.env.PORT ?? 5090);
const gateway = createGateway({
  client: createFaturaClient(env),
  state: new StateStore(process.env.STATE_FILE ?? "data/gateway-state.json"),
  user: process.env.GIB_USER, password: process.env.GIB_PASSWORD, token, env,
  log: (k, m) => console.error(`[${new Date().toISOString()}] ${k}: ${m}`), // istek gövdesi/kimlik bilgisi yazılmaz
});
gateway.server.listen(port, bind, () => console.log(`Fatura ağ geçidi dinliyor: http://${bind}:${port}  (GİB ortamı: ${env})`));
