// Prisma 7 client for the docker SQL Server, via the mssql driver adapter.
// The SA password is read from the repo-root .env (SA_PASSWORD) — never hardcoded.
import fs from "node:fs";
import { PrismaMssql } from "@prisma/adapter-mssql";
import { PrismaClient } from "../generated/prisma/client";
import { ENV_FILE } from "../paths";

function readSaPassword(): string {
  const txt = fs.readFileSync(ENV_FILE, "utf8");
  for (const line of txt.split(/\r?\n/)) {
    const m = line.match(/^\s*SA_PASSWORD\s*=\s*(.*)$/);
    if (m) return m[1].trim();
  }
  throw new Error(`SA_PASSWORD not found in ${ENV_FILE}`);
}

const adapter = new PrismaMssql({
  server: process.env.DB_HOST ?? "127.0.0.1",
  port: Number(process.env.DB_PORT ?? 11433),
  database: process.env.DB_NAME ?? "TGame_gsp",
  user: process.env.DB_USER ?? "sa",
  password: readSaPassword(),
  options: { encrypt: false, trustServerCertificate: true },
});

export const prisma = new PrismaClient({ adapter });
