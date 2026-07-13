import { useEffect, useRef, useState } from "react";

const PAGE = 240;

/** Infinite-scroll window: renders `limit` rows, grows when the sentinel shows.
 *  `resetKey` resets the window back to one page (e.g. when the filter changes). */
export function useIncremental(resetKey: string, page = PAGE) {
  const [limit, setLimit] = useState(page);
  const sentinel = useRef<HTMLDivElement>(null);

  useEffect(() => setLimit(page), [resetKey, page]);

  useEffect(() => {
    const el = sentinel.current;
    if (!el) return;
    const io = new IntersectionObserver((entries) => {
      if (entries[0].isIntersecting) setLimit((l) => l + page);
    });
    io.observe(el);
    return () => io.disconnect();
  }, [resetKey, page]);

  return { limit, sentinel };
}
