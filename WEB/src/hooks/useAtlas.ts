import { useJson } from "./useJson";

export const useAtlas = () => useJson<number[]>("atlas.json");
