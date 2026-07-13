import type { DiffData } from "../types";
import { useJson } from "./useJson";

export const useDiff = () => useJson<DiffData>("diff.json");
