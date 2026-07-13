import { defineConfig } from "prisma/config";

// Prisma 7 config. `generate` only needs the schema location; the live DB
// connection is supplied at runtime by the mssql adapter in script/db/client.ts.
export default defineConfig({
  schema: "schema.prisma", // relative to this config file (prisma/)
});
