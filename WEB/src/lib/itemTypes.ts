export function typeName(types: Record<string, string>, t: number): string {
  return types[String(t)] ?? `Type ${t}`;
}

const hue = (t: number): number => (t * 47) % 360;

/** Deterministic per-type color for placeholder tiles / badges. */
export function typeColor(t: number): string {
  return `hsl(${hue(t)} 45% 32%)`;
}
