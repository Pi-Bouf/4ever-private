import type { Mount } from "../types";
import { useJson } from "./useJson";

export const useMounts = () => useJson<Mount[]>("mounts.json");
