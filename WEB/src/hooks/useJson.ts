import { useEffect, useState } from "react";
import { dataUrl } from "../lib/iconUrl";

export interface Loadable<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
}

/** Fetch a JSON file from public/ once. */
export function useJson<T>(file: string): Loadable<T> {
  const [state, setState] = useState<Loadable<T>>({ data: null, loading: true, error: null });
  useEffect(() => {
    let alive = true;
    fetch(dataUrl(file))
      .then((r) => {
        if (!r.ok) throw new Error(`${r.status} loading ${file}`);
        return r.json() as Promise<T>;
      })
      .then((data) => alive && setState({ data, loading: false, error: null }))
      .catch((e: unknown) => alive && setState({ data: null, loading: false, error: String(e) }));
    return () => {
      alive = false;
    };
  }, [file]);
  return state;
}
