// Import this FIRST in an entry point: loads the repo-root .env (then backend/.env).
import { loadEnv } from "./env.js";

export const loadedEnvFiles = loadEnv();
