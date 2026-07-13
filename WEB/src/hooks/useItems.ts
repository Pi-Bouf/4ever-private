import type { Item } from "../types";
import { useJson } from "./useJson";

export const useItems = () => useJson<Item[]>("items.json");
