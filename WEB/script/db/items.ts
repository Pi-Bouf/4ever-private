// Item list + names + stats, read from SQL Server (TITEMCHART) via Prisma.
import { prisma } from "./client";

export interface DbItem {
  id: number;
  type: number;
  kind: number | null;
  name: string;
  level: number | null;
  price: number | null;
  dura: number | null;
  refine: number | null;
  stack: number | null;
  attrId: number | null;
  slotId: number | null;
  classId: number | null;
  useValue: number | null;
  useTime: number | null;
}

export async function getItems(): Promise<Map<number, DbItem>> {
  const rows = await prisma.titemChart.findMany({
    select: {
      wItemID: true,
      bType: true,
      bKind: true,
      szNAME: true,
      bLevel: true,
      fPrice: true,
      dwDuraMax: true,
      bRefineMax: true,
      bStack: true,
      wAttrID: true,
      dwSlotID: true,
      dwClassID: true,
      wUseValue: true,
      wUseTime: true,
    },
    orderBy: { wItemID: "asc" },
  });

  const map = new Map<number, DbItem>();
  for (const r of rows) {
    map.set(r.wItemID, {
      id: r.wItemID,
      type: r.bType,
      kind: r.bKind,
      name: (r.szNAME ?? "").trim(),
      level: r.bLevel,
      price: r.fPrice != null ? Math.round(r.fPrice) : null,
      dura: r.dwDuraMax,
      refine: r.bRefineMax,
      stack: r.bStack,
      attrId: r.wAttrID,
      slotId: r.dwSlotID,
      classId: r.dwClassID,
      useValue: r.wUseValue,
      useTime: r.wUseTime,
    });
  }
  return map;
}
