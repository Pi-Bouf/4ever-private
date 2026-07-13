import type { Meta } from "../types";
import { useJson } from "./useJson";

export const useMeta = () => useJson<Meta>("meta.json");
