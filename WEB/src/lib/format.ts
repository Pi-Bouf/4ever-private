import type { ItemDetail } from "../types";

export type StatRow = [label: string, value: string];

/** The detail fields an item actually has, as [label, value] rows. */
export function statRows(d: ItemDetail): StatRow[] {
  const rows: StatRow[] = [];
  const push = (label: string, value: string | number | null | undefined): void => {
    if (value != null && value !== "") rows.push([label, String(value)]);
  };
  push("Required Level", d.level);
  push("Attack (AP)", d.ap?.join("–"));
  push("Defense (DP)", d.dp);
  push("Magic Attack", d.map?.join("–"));
  push("Magic Defense", d.mdp);
  push("Block", d.block != null ? `${d.block}%` : undefined);
  push("Attack Speed", d.speed);
  push("Range", d.range && d.range[1] ? d.range.join("–") : undefined);
  push("Durability", d.dura?.toLocaleString());
  push("Max Stack", d.stack);
  push("Max Refine", d.refine);
  push("Price", d.price != null ? `${d.price.toLocaleString()} gold` : undefined);
  push("Use value", d.useValue);
  push("Duration", d.useTime);
  return rows;
}
