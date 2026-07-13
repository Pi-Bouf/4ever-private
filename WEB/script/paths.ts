// Filesystem paths. `script/` lives at WEB/script; the game tree and the repo
// `.env` (SA_PASSWORD) are at the repo root (WEB's parent).
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url)); // WEB/script
export const REPO_ROOT = path.resolve(here, "..", ".."); // repo root
export const GAME = path.join(REPO_ROOT, "Game");
export const PUBLIC = path.resolve(here, "..", "public"); // WEB/public
export const ICONS = path.join(PUBLIC, "icons");
export const ENV_FILE = path.join(REPO_ROOT, ".env");
export const CUSTOM = path.join(GAME, "Data", "Img", "Custom");
export const MIGRATIONS = path.join(REPO_ROOT, "Database", "migrations"); // tracked SQL migrations
export const MISSING_SQL_FILE = "missing-mountpet-items.sql"; // basename under PUBLIC (download)

export const GAME_FILES = {
  items: path.join(GAME, "Tcd", "TItem.tcd"),
  visuals: path.join(GAME, "Tcd", "TItemVisual.tcd"),
  attrs: path.join(GAME, "Tcd", "TItemAttr.tcd"),
  info: path.join(GAME, "Tcd", "TInfo.tcd"),
  mounts: path.join(GAME, "Tcd", "TMount.tcd"),
  idx: path.join(GAME, "Index", "TClientI.IDX"),
  tif: path.join(GAME, "TClientCmd.tif"),
};
