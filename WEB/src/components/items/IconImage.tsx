import { iconUrl } from "../../lib/iconUrl";
import { PixelImg } from "../ui/PixelImg";

export function IconImage({ id, size = 40, className = "" }: { id: number; size?: number; className?: string }) {
  return <PixelImg src={iconUrl(id)} size={size} className={className} />;
}
