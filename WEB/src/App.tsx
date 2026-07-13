import { useMemo, useState } from "react";
import type { Item } from "./types";
import { useItems } from "./hooks/useItems";
import { useMeta } from "./hooks/useMeta";
import { useAtlas } from "./hooks/useAtlas";
import { useDiff } from "./hooks/useDiff";
import { useMounts } from "./hooks/useMounts";
import { useMissing } from "./hooks/useMissing";
import { useDebounce } from "./hooks/useDebounce";
import { useFilteredItems } from "./hooks/useFilteredItems";
import { Container } from "./components/layout/Container";
import { Header } from "./components/layout/Header";
import { TabBar } from "./components/layout/TabBar";
import { Tab } from "./components/layout/Tab";
import { ItemsBanner } from "./components/layout/ItemsBanner";
import { Footer } from "./components/layout/Footer";
import { FilterBar } from "./components/controls/FilterBar";
import { SearchBox } from "./components/controls/SearchBox";
import { TypeFilter } from "./components/controls/TypeFilter";
import { IconToggle } from "./components/controls/IconToggle";
import { ResultCount } from "./components/controls/ResultCount";
import { ItemsView } from "./components/items/ItemsView";
import { AtlasView } from "./components/atlas/AtlasView";
import { DiffView } from "./components/diff/DiffView";
import { MountsPetsView } from "./components/mountspets/MountsPetsView";
import { ItemModal } from "./components/detail/ItemModal";
import { Spinner } from "./components/common/Spinner";
import { ErrorBoundary } from "./components/common/ErrorBoundary";

type View = "items" | "atlas" | "diff" | "mountspets";

export default function App() {
  const { data: items } = useItems();
  const { data: meta } = useMeta();
  const { data: atlas } = useAtlas();
  const { data: diff } = useDiff();
  const { data: mounts } = useMounts();
  const { data: missing } = useMissing();

  const [view, setView] = useState<View>("items");
  const [q, setQ] = useState("");
  const [type, setType] = useState("all");
  const [showIcons, setShowIcons] = useState(true);
  const [selected, setSelected] = useState<Item | null>(null);

  const dq = useDebounce(q, 120);
  const filtered = useFilteredItems(items ?? [], dq, type);
  const needle = dq.trim();
  const filteredAtlas = (atlas ?? []).filter((id) => !needle || String(id).includes(needle));
  const types = meta?.types ?? {};
  const itemsById = useMemo(() => new Map((items ?? []).map((it) => [it.id, it] as const)), [items]);

  if (!items) {
    return (
      <Container>
        <Header meta={meta} />
        <Spinner label="Loading item catalog…" />
      </Container>
    );
  }

  return (
    <ErrorBoundary>
      <Container>
        <Header meta={meta} />

        <TabBar>
          <Tab active={view === "items"} onClick={() => setView("items")}>
            Items ({items.length.toLocaleString()})
          </Tab>
          <Tab active={view === "atlas"} onClick={() => setView("atlas")}>
            Icon Atlas ({(atlas ?? []).length.toLocaleString()})
          </Tab>
          <Tab active={view === "diff"} onClick={() => setView("diff")}>
            DB vs Game ({diff ? diff.entries.length.toLocaleString() : "…"})
          </Tab>
          <Tab active={view === "mountspets"} onClick={() => setView("mountspets")}>
            Mounts &amp; Pets ({mounts ? mounts.length.toLocaleString() : "…"})
          </Tab>
        </TabBar>

        {view === "items" && meta && <ItemsBanner meta={meta} />}

        <FilterBar>
          <SearchBox
            value={q}
            onChange={setQ}
            placeholder={
              view === "items"
                ? "Search items by name or id…"
                : view === "atlas"
                  ? "Search icons by id…"
                  : view === "diff"
                    ? "Search differences by name or id…"
                    : "Search mounts & pets by name or id…"
            }
          />
          {view === "items" && (
            <>
              <TypeFilter value={type} onChange={setType} types={types} />
              <IconToggle checked={showIcons} onChange={setShowIcons} />
              <ResultCount count={filtered.length} />
            </>
          )}
          {view === "atlas" && <ResultCount count={filteredAtlas.length} noun="icons" />}
        </FilterBar>

        {view === "items" && (
          <ItemsView
            items={filtered}
            types={types}
            showIcons={showIcons}
            onSelect={setSelected}
            resetKey={`${dq}|${type}`}
          />
        )}
        {view === "atlas" && <AtlasView ids={filteredAtlas} resetKey={dq} />}
        {view === "diff" &&
          (diff ? <DiffView diff={diff} query={dq} types={types} /> : <Spinner label="Loading differences…" />)}
        {view === "mountspets" &&
          (mounts ? (
            <MountsPetsView
              mounts={mounts}
              items={items}
              itemsById={itemsById}
              diff={diff ?? null}
              missing={missing ?? null}
              types={types}
              query={dq}
              showIcons={showIcons}
              onSelect={setSelected}
            />
          ) : (
            <Spinner label="Loading mounts…" />
          ))}

        <Footer meta={meta} />

        {selected && <ItemModal item={selected} types={types} onClose={() => setSelected(null)} />}
      </Container>
    </ErrorBoundary>
  );
}
