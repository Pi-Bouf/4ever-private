const BASE = import.meta.env.BASE_URL;

export const iconUrl = (id: number): string => `${BASE}icons/${id}.png`;
export const dataUrl = (name: string): string => `${BASE}${name}`;
