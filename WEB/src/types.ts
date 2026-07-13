export interface ItemDetail {
  level?: number;
  price?: number;
  dura?: number;
  stack?: number;
  refine?: number;
  slot?: number;
  cls?: number;
  useValue?: number;
  useTime?: number;
  range?: [number, number];
  ap?: number[]; // [max] or [min,max]
  dp?: number;
  map?: number[];
  mdp?: number;
  block?: number;
  speed?: number;
  flags?: string[];
  desc?: string;
}

export interface Item {
  id: number;
  name: string;
  type: number;
  kind: number;
  iconBest: number | null;
  det?: ItemDetail;
}

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
  types: Record<string, string>;
  byType: Record<string, number>;
}

// DB (TITEMCHART) vs game files (TItem.tcd) differences.
export type DiffKind = "onlyDb" | "onlyFile" | "changed";

export interface FieldDiff {
  field: string;
  db: string | number | null;
  file: string | number | null;
}

export interface DiffEntry {
  id: number;
  name: string;
  type: number;
  kind: DiffKind;
  fields?: FieldDiff[];
}

export interface DiffSummary {
  dbTotal: number;
  fileTotal: number;
  onlyDb: number;
  onlyFile: number;
  changed: number;
  identical: number;
}

export interface DiffData {
  summary: DiffSummary;
  entries: DiffEntry[];
}

// Items present in the client data (TItem.tcd) but missing from the DB
// (TITEMCHART) — the source of the generated "add missing items" SQL migration.
export interface MissingEntry {
  id: number;
  name: string;
  type: number;
  siblingId: number;
}

export interface MissingData {
  scope: string;
  count: number;
  generatedAt: string;
  migrationFile: string;
  sqlFile: string;
  entries: MissingEntry[];
}

// Mount roster (TMount.tcd) + the IT_PET items that grant each mount.
export interface Mount {
  id: number;
  monId: number;
  saddleMonId: number;
  iconIdx: number;
  icon: number | null;
  name: string;
  items: number[];
}
