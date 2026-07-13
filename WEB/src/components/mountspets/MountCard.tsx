import type { Mount } from "../../types";
import { iconUrl } from "../../lib/iconUrl";
import { Button } from "../ui/Button";
import { PixelImg } from "../ui/PixelImg";
import { PlaceholderIcon } from "../items/PlaceholderIcon";

export function MountCard({ mount, onClick }: { mount: Mount; onClick: () => void }) {
  return (
    <Button
      onClick={onClick}
      className="flex min-h-[56px] items-center gap-3 rounded-[10px] border border-border bg-panel px-2.5 py-2 text-left transition-colors hover:border-borderlt hover:bg-panel2"
    >
      <div className="grid shrink-0 place-items-center overflow-hidden rounded-[7px] bg-panel2" style={{ width: 40, height: 40 }}>
        {mount.icon != null ? <PixelImg src={iconUrl(mount.icon)} size={40} /> : <PlaceholderIcon id={mount.id} type={12} size={40} />}
      </div>
      <div className="min-w-0">
        <div className="truncate text-[13.5px] font-semibold" title={mount.name}>
          {mount.name}
        </div>
        <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px] text-muted">
          <span>mount #{mount.id}</span>
          <span>mon {mount.monId}</span>
          {mount.items.length > 1 && <span>{mount.items.length} items</span>}
        </div>
      </div>
    </Button>
  );
}
