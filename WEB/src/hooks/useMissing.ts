import type { MissingData } from "../types";
import { useJson } from "./useJson";

export const useMissing = () => useJson<MissingData>("missing.json");
