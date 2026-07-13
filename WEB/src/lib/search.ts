import type { Item } from "../types";

/** Parse a query into a lowercased needle + optional exact numeric id. */
export function parseQuery(q: string): { needle: string; id: number | null } {
  const needle = q.trim().toLowerCase();
  const id = /^\d+$/.test(needle) ? Number(needle) : null;
  return { needle, id };
}

export function matchItem(it: Item, needle: string, id: number | null): boolean {
  if (!needle) return true;
  if (id != null && it.id === id) return true;
  return it.name.toLowerCase().includes(needle) || String(it.id).includes(needle);
}
