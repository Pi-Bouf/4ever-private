import { Badge } from "../common/Badge";
import { typeName } from "../../lib/itemTypes";

export function TypeBadge({ type, types }: { type: number; types: Record<string, string> }) {
  return <Badge>{typeName(types, type)}</Badge>;
}
