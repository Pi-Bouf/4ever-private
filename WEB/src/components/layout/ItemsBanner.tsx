import type { Meta } from "../../types";
import { Banner } from "./Banner";
import { Strong } from "../ui/Strong";
import { Code } from "../ui/Code";

export function ItemsBanner({ meta }: { meta: Meta }) {
  return (
    <Banner>
      <Strong className="text-[#d6f5dd]">Items via Prisma</Strong> — the list, names and stats come from SQL Server{" "}
      <Code>TITEMCHART</Code>. Icons reproduce the client exactly: <Code>m_wVisual[0] → TItemVisual.m_wIcon</Code>{" "}
      (index) → <Code>TClientCmd.tif</Code> slot list → atlas sprite. {meta.itemsWithIcon.toLocaleString()} of{" "}
      {meta.totalItems.toLocaleString()} items have an icon.
    </Banner>
  );
}
