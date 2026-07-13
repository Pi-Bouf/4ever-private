import fs from "node:fs";
import path from "node:path";
import { PUBLIC, MIGRATIONS, MISSING_SQL_FILE } from "../paths";
import type { OutItem } from "./build";
import type { DiffData } from "./diff";
import type { Mount } from "./mounts";
import type { MissingEntry } from "./missing";

export interface Meta {
  generatedAt: string;
  totalItems: number;
  named: number;
  itemsWithIcon: number;
  galleryIcons: number;
  customIcons: number;
  itemSource: string;
  nameSource: string;
  iconSource: string;
  types: Record<number, string>;
  byType: Record<string, number>;
}

export function writeOutput(items: OutItem[], gallery: number[], meta: Meta): void {
  fs.mkdirSync(PUBLIC, { recursive: true });
  fs.writeFileSync(path.join(PUBLIC, "items.json"), JSON.stringify(items));
  fs.writeFileSync(path.join(PUBLIC, "atlas.json"), JSON.stringify(gallery));
  fs.writeFileSync(path.join(PUBLIC, "meta.json"), JSON.stringify(meta, null, 2));
}

export function writeDiff(diff: DiffData): void {
  fs.mkdirSync(PUBLIC, { recursive: true });
  fs.writeFileSync(path.join(PUBLIC, "diff.json"), JSON.stringify(diff));
}

export function writeMounts(mounts: Mount[]): void {
  fs.mkdirSync(PUBLIC, { recursive: true });
  fs.writeFileSync(path.join(PUBLIC, "mounts.json"), JSON.stringify(mounts));
}

export interface MissingData {
  scope: string;
  count: number;
  generatedAt: string;
  migrationFile: string;
  sqlFile: string;
  entries: MissingEntry[];
}

/**
 * Write the "missing items" artifacts: always the public SQL + missing.json (for
 * the UI Download button); if `promote`, also lay down the tracked migration under
 * Database/migrations/ — but only if it does not already exist, so re-runs never
 * mutate an applied migration.
 *
 * A UTF-8 BOM is prepended ONLY when the SQL contains non-ASCII text (so sqlcmd,
 * which the runner invokes without an explicit codepage, reads the N'...' names as
 * Unicode). All-ASCII migrations stay BOM-free, matching the existing 00x files.
 */
export function writeMissing(
  sql: string,
  entries: MissingEntry[],
  opts: { scope: string; migrationName: string; promote?: boolean },
): void {
  fs.mkdirSync(PUBLIC, { recursive: true });
  const migrationFile = `${opts.migrationName}.sql`;
  const out = /[^\x00-\x7F]/.test(sql) ? "﻿" + sql : sql;
  const data: MissingData = {
    scope: opts.scope,
    count: entries.length,
    generatedAt: new Date().toISOString(),
    migrationFile,
    sqlFile: MISSING_SQL_FILE,
    entries,
  };
  fs.writeFileSync(path.join(PUBLIC, MISSING_SQL_FILE), out);
  fs.writeFileSync(path.join(PUBLIC, "missing.json"), JSON.stringify(data));

  if (opts.promote) {
    fs.mkdirSync(MIGRATIONS, { recursive: true });
    const dest = path.join(MIGRATIONS, migrationFile);
    if (fs.existsSync(dest)) {
      console.log(`  migration ${migrationFile} already exists — left unchanged`);
    } else {
      fs.writeFileSync(dest, out);
      console.log(`  wrote Database/migrations/${migrationFile}`);
    }
  }
}
