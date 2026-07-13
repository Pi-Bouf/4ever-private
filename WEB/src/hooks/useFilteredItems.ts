import { useMemo } from "react";
import type { Item } from "../types";
import { matchItem, parseQuery } from "../lib/search";

export function useFilteredItems(items: Item[], query: string, type: string): Item[] {
  return useMemo(() => {
    const { needle, id } = parseQuery(query);
    return items.filter((it) => {
      if (type !== "all" && String(it.type) !== type) return false;
      return matchItem(it, needle, id);
    });
  }, [items, query, type]);
}
