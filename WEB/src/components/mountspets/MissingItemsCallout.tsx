import type { MissingData } from "../../types";
import { dataUrl } from "../../lib/iconUrl";
import { DownloadButton } from "../ui/DownloadButton";

/** Banner shown above the DB-vs-TCD diff: how many mount/pet items are missing
 *  from the database, with a button to download the generated SQL migration. */
export function MissingItemsCallout({ missing }: { missing: MissingData }) {
  if (missing.count === 0) return null;
  return (
    <div className="mb-4 flex flex-col gap-2.5 rounded-[10px] border border-accent2/40 bg-accent2/5 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="text-[12.5px]">
        <span className="font-semibold text-text">{missing.count}</span>{" "}
        <span className="text-muted">
          mount/pet item{missing.count === 1 ? "" : "s"} exist in the game files but {missing.count === 1 ? "is" : "are"}{" "}
          missing from the database. The migration clones a same-type row per item and overwrites the game-file fields.
        </span>
      </div>
      <DownloadButton href={dataUrl(missing.sqlFile)} filename={missing.migrationFile} className="shrink-0">
        ⬇ Download SQL migration
      </DownloadButton>
    </div>
  );
}
