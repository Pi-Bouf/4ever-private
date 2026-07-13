// Standalone, OFFLINE mount extraction (no DB / Prisma). Reads TMount.tcd +
// the IT_PET items from the game files and writes public/mounts.json, augmenting
// public/icons without clearing it. Run: `npm run extract:mounts`.
import fs from "node:fs";
import { loadGameData } from "./steps/gameData";
import { IconWriter } from "./steps/icons";
import { buildMounts } from "./steps/mounts";
import { writeMounts } from "./steps/write";
import { ICONS } from "./paths";

const g = loadGameData();
fs.mkdirSync(ICONS, { recursive: true }); // augment, don't clear
const writer = new IconWriter(g);
const mounts = buildMounts(g, writer);
writeMounts(mounts);

const withIcon = mounts.filter((m) => m.icon != null).length;
const withItems = mounts.filter((m) => m.items.length > 0).length;
console.log(`Mounts : ${mounts.length} definitions (${withIcon} with icon, ${withItems} with a granting item)`);
console.log(`Wrote  : public/mounts.json (+${writer.written.size} icons touched)`);
